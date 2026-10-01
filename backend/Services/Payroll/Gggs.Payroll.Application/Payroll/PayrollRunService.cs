using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

/// <summary>
/// Creating a run, calculating it against every employee's currently assigned salary, and
/// finalizing it. No attendance-based proration, statutory deduction or payslip generation in this
/// v1 slice - see <see cref="PayrollRun"/>'s own remarks. Gross is the sum of that employee's
/// Earning components; net is gross minus the sum of Deduction components - the same split
/// <see cref="SalaryComponentKind"/> already carries, nothing new to configure.
/// </summary>
public sealed class PayrollRunService(
    IPayrollRunRepository runs,
    IPayrollRunEmployeeRepository runEmployees,
    IPayrollRunLineRepository runLines,
    IPayPeriodRepository periods,
    IEmployeeSalaryRepository salaries,
    IEmployeeSalaryComponentRepository salaryComponents,
    ISalaryComponentRepository components,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PayrollRun> CreateAsync(Guid payGroupId, Guid payPeriodId, string runNo, CancellationToken cancellationToken = default)
    {
        PayPeriod period = await periods.GetAsync(payPeriodId, cancellationToken)
            ?? throw new NotFoundException(nameof(PayPeriod), payPeriodId);

        Guard(
            period.Status == PayPeriodStatus.Open,
            "PAY_PERIOD_NOT_OPEN_FOR_RUN",
            $"Period '{period.PeriodCode}' must be Open to start a run against it; it is {period.Status}.");

        Guard(
            !await runs.RunNoExistsAsync(runNo, cancellationToken),
            "PAYROLL_RUN_NO_ALREADY_EXISTS",
            $"A run with number '{runNo}' already exists.");

        PayrollRun run = PayrollRun.Create(payGroupId, payPeriodId, runNo);
        runs.Add(run);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>
    /// Snapshots every employee's current salary breakdown into this run, replacing whatever this
    /// run held from a previous calculation. Iterates every <see cref="EmployeeSalary"/> in the
    /// tenant - see <see cref="IEmployeeSalaryRepository.ListAllCurrentAsync"/>'s own remarks on why
    /// there is no narrower per-pay-group roster yet.
    /// </summary>
    public async Task<PayrollRun> CalculateAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        PayrollRun run = await runs.GetAsync(runId, cancellationToken) ?? throw new NotFoundException(nameof(PayrollRun), runId);

        Guard(
            run.Status != PayrollRunStatus.Finalized,
            "PAYROLL_RUN_ALREADY_FINALIZED",
            $"Run '{run.RunNo}' is already finalized and cannot be recalculated.");

        IReadOnlyList<PayrollRunEmployee> existingResults = await runEmployees.ListForRunAsync(runId, cancellationToken);
        if (existingResults.Count > 0)
        {
            IReadOnlyList<PayrollRunLine> existingLines = await runLines.ListForRunEmployeesAsync(
                existingResults.Select(x => x.Id), cancellationToken);
            runLines.RemoveAllForRunEmployees(existingLines);
            runEmployees.RemoveAllForRun(existingResults);
        }

        IReadOnlyList<EmployeeSalary> allSalaries = await salaries.ListAllCurrentAsync(cancellationToken);

        if (allSalaries.Count == 0)
        {
            run.RecordCalculation(clock.GetUtcNow().UtcDateTime, employeeCount: 0, grossTotal: 0, netTotal: 0);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return run;
        }

        IReadOnlyList<EmployeeSalaryComponent> allLines = await salaryComponents.ListForSalariesAsync(
            allSalaries.Select(x => x.Id), cancellationToken);

        IReadOnlyList<SalaryComponent> componentCatalog = await components.GetManyAsync(
            allLines.Select(x => x.ComponentId).Distinct(), cancellationToken);
        Dictionary<Guid, SalaryComponent> componentsById = componentCatalog.ToDictionary(x => x.Id);

        decimal runGross = 0;
        decimal runNet = 0;

        foreach (EmployeeSalary salary in allSalaries)
        {
            List<EmployeeSalaryComponent> lines = allLines.Where(x => x.EmployeeSalaryId == salary.Id).ToList();

            decimal gross = lines
                .Where(x => componentsById[x.ComponentId].Kind == SalaryComponentKind.Earning)
                .Sum(x => x.MonthlyAmount);

            decimal deductions = lines
                .Where(x => componentsById[x.ComponentId].Kind == SalaryComponentKind.Deduction)
                .Sum(x => x.MonthlyAmount);

            decimal netPay = gross - deductions;

            PayrollRunEmployee runEmployee = PayrollRunEmployee.Create(runId, salary.EmployeeId, gross, netPay);
            runEmployees.Add(runEmployee);

            foreach (EmployeeSalaryComponent line in lines)
            {
                runLines.Add(PayrollRunLine.Create(runEmployee.Id, line.ComponentId, line.MonthlyAmount));
            }

            runGross += gross;
            runNet += netPay;
        }

        run.RecordCalculation(clock.GetUtcNow().UtcDateTime, allSalaries.Count, runGross, runNet);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>Calculated -> Finalized, and locks+closes the run's own period in the same
    /// operation - a finalized run's period can never accept another run.</summary>
    public async Task<PayrollRun> FinalizeAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        PayrollRun run = await runs.GetAsync(runId, cancellationToken) ?? throw new NotFoundException(nameof(PayrollRun), runId);
        PayPeriod period = await periods.GetAsync(run.PayPeriodId, cancellationToken)
            ?? throw new NotFoundException(nameof(PayPeriod), run.PayPeriodId);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        run.Finalize(now);
        period.Lock();
        period.Close();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return run;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
