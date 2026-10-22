using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Payroll.Contracts;

/// <summary>One line of a salary breakdown: a component's monthly and annual amount.</summary>
public sealed record SalaryLine(string Code, string Name, string Kind, decimal Monthly, decimal Annual);

/// <summary>An employee's salary as of a day: the CTC, the structure it is laid out by, and the breakdown.</summary>
public sealed record SalaryInfo(
    Guid SalaryId,
    Guid EmployeeId,
    decimal AnnualCtc,
    string Currency,
    DateOnly EffectiveFrom,
    Guid StructureId,
    string StructureName,
    decimal MonthlyGross,
    decimal MonthlyBasic,
    IReadOnlyList<SalaryLine> Lines);

/// <summary>A new CTC from a day - a revision, promotion or correction decided elsewhere (Compensation).</summary>
public sealed record SalaryRevisionRequest(
    Guid EmployeeId,
    decimal AnnualCtc,
    DateOnly EffectiveFrom,
    Guid? StructureId,
    string Reason,
    string Source,
    Guid? SourceId);

/// <summary>Salaries for compensation and total rewards (ADR-12). Payroll stays the owner of who is paid what.</summary>
public interface IPayrollSalaries
{
    /// <summary>The salary in force on <paramref name="day"/> (today when null) for each employee who has one.</summary>
    Task<IReadOnlyDictionary<Guid, SalaryInfo>> CurrentAsync(IEnumerable<Guid> employeeIds, DateOnly? day, CancellationToken cancellationToken = default);

    /// <summary>Records a new salary from the request's date, closing the one before it. Returns the new salary's id.</summary>
    Task<Guid> ReviseAsync(SalaryRevisionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What a one-off payroll input pays or recovers.</summary>
public enum PayrollInputKind
{
    Bonus = 1,
    Incentive = 2,
    Commission = 3,
    Overtime = 4,
    Arrears = 5,
    Reimbursement = 6,
    Deduction = 7,

    /// <summary>Gratuity paid at exit, within the tax-free limit.</summary>
    Gratuity = 8,

    /// <summary>Earned leave encashed at exit, within the tax-free limit.</summary>
    LeaveEncashment = 9,

    /// <summary>Wages paid instead of notice the company did not give (taxable).</summary>
    NoticePay = 10,

    /// <summary>Retrenchment compensation, within the tax-free limit.</summary>
    RetrenchmentCompensation = 11,

    /// <summary>Terminal benefits above their tax-free limits, and other taxable exit payments.</summary>
    TerminalTaxable = 12,
}

/// <summary>A one-off amount for a month's payroll, raised by another module (a bonus award, a project incentive).</summary>
public sealed record PayrollInputRequest(
    Guid EmployeeId,
    DateOnly Period,
    PayrollInputKind Kind,
    decimal Amount,
    string Description,
    string Source,
    Guid SourceId);

/// <summary>Variable inputs from other modules (spec §21, §94). Payroll remains the calculation engine.</summary>
public interface IPayrollInputs
{
    /// <summary>Adds the input to the period's payroll (the first open month on or after <see cref="PayrollInputRequest.Period"/>).</summary>
    Task<Guid> AddAsync(PayrollInputRequest request, CancellationToken cancellationToken = default);

    /// <summary>Withdraws a source's input if it has not been paid yet. Returns whether it was withdrawn.</summary>
    Task<bool> WithdrawAsync(string source, Guid sourceId, CancellationToken cancellationToken = default);
}

/// <summary>A source's input that a finalized payroll paid.</summary>
public sealed record PaidInput(string Source, Guid SourceId, Guid EmployeeId, decimal Amount);

/// <summary>Monthly payroll, or an off-cycle run that pays one leaver's full and final settlement.</summary>
public enum PayrollRunKind
{
    Regular = 1,
    FullAndFinal = 2,
}

/// <summary>
/// A payroll run was finalized: payslips are out, and these inputs have been paid. A full and final
/// run names the settlement it pays (<paramref name="SourceId"/>).
/// </summary>
[MessageType("gggs.payroll.run-finalized")]
public sealed record PayrollRunFinalizedIntegrationEvent(
    Guid RunId,
    DateOnly Period,
    int Employees,
    decimal NetPay,
    IReadOnlyList<PaidInput> PaidInputs,
    PayrollRunKind Kind = PayrollRunKind.Regular,
    Guid? SourceId = null) : IntegrationEvent;

/// <summary>A finalized payroll run was paid (the bank transfer went out).</summary>
[MessageType("gggs.payroll.run-paid")]
public sealed record PayrollRunPaidIntegrationEvent(Guid RunId, PayrollRunKind Kind, Guid? SourceId, DateOnly PaidOn, string? Reference) : IntegrationEvent;

/// <summary>One amount of a full and final settlement, as payroll pays it.</summary>
public sealed record FinalSettlementLine(PayrollInputKind Kind, decimal Amount, string Description);

/// <summary>
/// A leaver's full and final settlement to pay through payroll: the exit month's salary (worked out
/// by the payroll engine, with PF, ESI, PT and TDS) plus these lines.
/// </summary>
public sealed record FinalSettlementRequest(Guid EmployeeId, Guid SettlementId, DateOnly LastWorkingDay, IReadOnlyList<FinalSettlementLine> Lines);

/// <summary>Where a settlement stands in payroll.</summary>
public sealed record FinalSettlementRun(Guid RunId, DateOnly Period, string Status, decimal Gross, decimal Deductions, decimal Net, DateOnly PayDate, bool SalaryIncluded, IReadOnlyList<string> Issues);

/// <summary>
/// Full and final settlements through payroll (Separation): an off-cycle run for one leaver,
/// calculated at once and then approved, finalized and paid like any other run.
/// </summary>
public interface IFinalSettlementPayroll
{
    /// <summary>Opens (or reopens) the settlement's run and calculates it.</summary>
    Task<FinalSettlementRun> SubmitAsync(FinalSettlementRequest request, CancellationToken cancellationToken = default);

    Task<FinalSettlementRun?> FindAsync(Guid settlementId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the settlement's run unless it is finalized; returns false if there is none to cancel.</summary>
    Task<bool> CancelAsync(Guid settlementId, CancellationToken cancellationToken = default);
}
