using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

public enum PayrollRunStatus
{
    Draft = 1,
    Calculated = 2,
    Finalized = 3,
}

/// <summary>
/// One calculation of a <see cref="PayGroup"/>'s <see cref="PayPeriod"/> - Draft -&gt; Calculated
/// (repeatable) -&gt; Finalized (terminal). No RunType (Regular/OffCycle/Supplementary/FnF/Reversal)
/// in this v1 slice (architecture.md's <c>PayrollRun.RunType</c>): every run here is a regular run,
/// and no workflow approval gate yet - <see cref="Finalize"/> is a direct action, not something
/// reacting to a <c>WorkflowApproved</c> event the way <see cref="Attendance.AttendanceRegularization"/>
/// (a different service) is. Calculating snapshots each employee's <em>current</em>
/// <see cref="EmployeeSalary"/> breakdown, prorated against attendance for the period (see
/// <see cref="PayrollRunEmployee"/>) - no statutory deduction yet, so Calculated may be repeated
/// freely as salaries or attendance change, right up until Finalized locks the period for good.
/// </summary>
public sealed class PayrollRun : TenantAggregateRoot
{
    private PayrollRun()
    {
        RunNo = null!;
    }

    public Guid PayGroupId { get; private set; }

    public Guid PayPeriodId { get; private set; }

    public string RunNo { get; private set; }

    public PayrollRunStatus Status { get; private set; }

    public int EmployeeCount { get; private set; }

    public decimal GrossTotal { get; private set; }

    public decimal NetTotal { get; private set; }

    public DateTime? CalculatedDate { get; private set; }

    public DateTime? FinalizedDate { get; private set; }

    public static PayrollRun Create(Guid payGroupId, Guid payPeriodId, string runNo) => new()
    {
        PayGroupId = Guard.NotEmpty(payGroupId),
        PayPeriodId = Guard.NotEmpty(payPeriodId),
        RunNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(runNo).Trim(), 30),
        Status = PayrollRunStatus.Draft,
        IsActive = true,
    };

    /// <summary>Records the totals of a (re)calculation - callable from Draft or Calculated, never
    /// from Finalized, which is why this run's numbers can never silently drift after finalization.</summary>
    public void RecordCalculation(DateTime calculatedAtUtc, int employeeCount, decimal grossTotal, decimal netTotal)
    {
        Guard.Against(
            Status == PayrollRunStatus.Finalized, "PAYROLL_RUN_ALREADY_FINALIZED", $"Run '{RunNo}' is already finalized and cannot be recalculated.");

        Status = PayrollRunStatus.Calculated;
        EmployeeCount = employeeCount;
        GrossTotal = grossTotal;
        NetTotal = netTotal;
        CalculatedDate = calculatedAtUtc;
    }

    /// <summary>Calculated -> Finalized: terminal. The caller also locks and closes the run's own
    /// <see cref="PayPeriod"/> in the same operation - see <c>PayrollRunService.FinalizeAsync</c>.</summary>
    public void Finalize(DateTime finalizedAtUtc)
    {
        Guard.Against(
            Status != PayrollRunStatus.Calculated, "PAYROLL_RUN_NOT_CALCULATED", $"Run '{RunNo}' must be Calculated to finalize; it is {Status}.");

        Status = PayrollRunStatus.Finalized;
        FinalizedDate = finalizedAtUtc;
    }
}

/// <summary>One employee's result within a <see cref="PayrollRun"/> - replaced wholesale on every
/// recalculation (see <see cref="PayrollRunService"/>), never adjusted in place.
/// <para>
/// <see cref="PaidDays"/>/<see cref="LopDays"/>/<see cref="TotalDays"/> record the attendance-based
/// proration behind <see cref="Gross"/>/<see cref="NetPay"/>: every Earning-kind component is scaled
/// by <c>PaidDays / TotalDays</c> before the run's totals are summed, while Deduction-kind components
/// are left at their full monthly amount (there is no statutory-component concept yet to say
/// otherwise). <c>TotalDays == PaidDays</c> whenever Time has no attendance days on record for the
/// period, which keeps an employee fully paid rather than guessing at an LOP it cannot see.
/// </para></summary>
public sealed class PayrollRunEmployee : TenantAggregateRoot
{
    private PayrollRunEmployee()
    {
    }

    public Guid PayrollRunId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public decimal Gross { get; private set; }

    public decimal NetPay { get; private set; }

    public decimal PaidDays { get; private set; }

    public decimal LopDays { get; private set; }

    public decimal TotalDays { get; private set; }

    public static PayrollRunEmployee Create(
        Guid payrollRunId, Guid employeeId, decimal gross, decimal netPay, decimal paidDays, decimal lopDays, decimal totalDays) => new()
        {
            PayrollRunId = Guard.NotEmpty(payrollRunId),
            EmployeeId = Guard.NotEmpty(employeeId),
            Gross = gross,
            NetPay = netPay,
            PaidDays = paidDays,
            LopDays = lopDays,
            TotalDays = totalDays,
            IsActive = true,
        };
}

/// <summary>One component's amount within a <see cref="PayrollRunEmployee"/>'s result - the
/// per-component trace a payslip would eventually read from.</summary>
public sealed class PayrollRunLine : TenantAggregateRoot
{
    private PayrollRunLine()
    {
    }

    public Guid PayrollRunEmployeeId { get; private set; }

    public Guid ComponentId { get; private set; }

    public decimal Amount { get; private set; }

    public static PayrollRunLine Create(Guid payrollRunEmployeeId, Guid componentId, decimal amount) => new()
    {
        PayrollRunEmployeeId = Guard.NotEmpty(payrollRunEmployeeId),
        ComponentId = Guard.NotEmpty(componentId),
        Amount = amount,
        IsActive = true,
    };
}
