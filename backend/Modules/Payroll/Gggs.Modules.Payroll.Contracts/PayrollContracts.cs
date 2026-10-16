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

/// <summary>A payroll run was finalized: payslips are out, and these inputs have been paid.</summary>
[MessageType("gggs.payroll.run-finalized")]
public sealed record PayrollRunFinalizedIntegrationEvent(Guid RunId, DateOnly Period, int Employees, decimal NetPay, IReadOnlyList<PaidInput> PaidInputs) : IntegrationEvent;
