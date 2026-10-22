using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

/// <summary>
/// Pays a leaver's full and final settlement through payroll: an off-cycle run for the exit month
/// that the engine calculates like any other - the month's salary with PF, ESI, PT and TDS, plus the
/// settlement's terminal benefits and recoveries as inputs - and that is then approved, finalized
/// (payslip) and paid (bank file) through the usual run screens.
/// </summary>
internal sealed class FinalSettlementPayroll(
    AppDbContext dbContext,
    RunService runs,
    PayrollCatalog catalog,
    PayrollSeeder seeder,
    IEmployeeDirectory employees,
    ITenantClock clock) : IFinalSettlementPayroll
{
    /// <summary>The input source of a settlement's lines.</summary>
    public const string Source = "separation.settlement";

    public async Task<FinalSettlementRun> SubmitAsync(FinalSettlementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Tenants set up before full and final runs existed get the terminal-benefit components now.
        await seeder.SeedCurrentTenantAsync(cancellationToken);
        if (await OpenRunAsync(request.SettlementId, cancellationToken) is { } previous)
        {
            if (previous.IsLocked)
            {
                throw new DomainRuleException("SETTLEMENT_PAYROLL_FINAL", "The settlement's payroll run is already finalized.");
            }

            await runs.CancelAsync(previous.Id, cancellationToken);
        }

        EmployeeSummary person = await employees.GetAsync(request.EmployeeId, cancellationToken) ?? throw new DomainRuleException("SETTLEMENT_EMPLOYEE", "The employee could not be found.");
        var exitMonth = new DateOnly(request.LastWorkingDay.Year, request.LastWorkingDay.Month, 1);

        // When the exit month's regular payroll already paid the salary, the settlement pays only its
        // own lines - in the next month so that TDS counts that salary, unless that is a new tax year.
        bool salaryPaid = await (
                from item in dbContext.PayrollRunItems
                join run in dbContext.PayrollRuns on item.RunId equals run.Id
                where item.EmployeeId == request.EmployeeId && !item.OnHold && item.Status != ItemStatus.Error
                    && run.Kind == PayrollRunKind.Regular && run.Period == exitMonth && (run.Status == RunStatus.Finalized || run.Status == RunStatus.Paid)
                select item.Id)
            .AnyAsync(cancellationToken);
        DateOnly period = salaryPaid && exitMonth.Month != 3 ? exitMonth.AddMonths(1) : exitMonth;
        DateOnly today = await clock.TodayAsync(cancellationToken);
        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        PayrollRun settlementRun = PayrollRun.OpenFinalSettlement(
            period, today < period ? period : today, person.LegalEntityId, settings.DefaultCurrency, request.EmployeeId, request.SettlementId, request.LastWorkingDay, salaryPaid,
            $"Full and final settlement: {person.DisplayName}, last working day {request.LastWorkingDay:d MMMM yyyy}.");
        dbContext.PayrollRuns.Add(settlementRun);

        IReadOnlyList<SalaryComponent> components = await catalog.ComponentsAsync(cancellationToken);
        foreach (FinalSettlementLine line in (request.Lines ?? []).Where(line => line.Amount > 0))
        {
            SalaryComponent component = components.FirstOrDefault(row => row.IsActive && row.InputKind == line.Kind)
                ?? throw new DomainRuleException("INPUT_COMPONENT", $"No active payroll component takes {line.Kind} inputs.");
            var input = PayrollInput.Raise(request.EmployeeId, period, component, line.Kind, line.Amount, null, line.Description, Source, request.SettlementId);
            input.IncludeIn(settlementRun.Id);
            dbContext.PayrollInputs.Add(input);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await runs.CalculateAsync(settlementRun.Id, cancellationToken);
        return (await FindAsync(request.SettlementId, cancellationToken))!;
    }

    public async Task<FinalSettlementRun?> FindAsync(Guid settlementId, CancellationToken cancellationToken = default)
    {
        PayrollRun? run = await dbContext.PayrollRuns.AsNoTracking()
            .Where(row => row.Kind == PayrollRunKind.FullAndFinal && row.SourceId == settlementId && row.Status != RunStatus.Cancelled)
            .OrderByDescending(row => row.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        PayrollRunItem? item = await dbContext.PayrollRunItems.AsNoTracking().FirstOrDefaultAsync(row => row.RunId == run.Id, cancellationToken);
        return new FinalSettlementRun(run.Id, run.Period, run.Status.ToString(), run.Gross, run.Deductions, run.Net, run.PayDate, !run.SalaryExcluded, item?.Issues ?? []);
    }

    public async Task<bool> CancelAsync(Guid settlementId, CancellationToken cancellationToken = default)
    {
        PayrollRun? run = await OpenRunAsync(settlementId, cancellationToken);
        if (run is null)
        {
            return false;
        }

        if (run.IsLocked)
        {
            throw new DomainRuleException("SETTLEMENT_PAYROLL_FINAL", "The settlement's payroll run is already finalized.");
        }

        await runs.CancelAsync(run.Id, cancellationToken);
        return true;
    }

    private Task<PayrollRun?> OpenRunAsync(Guid settlementId, CancellationToken cancellationToken)
        => dbContext.PayrollRuns.AsNoTracking()
            .Where(row => row.Kind == PayrollRunKind.FullAndFinal && row.SourceId == settlementId && row.Status != RunStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);
}
