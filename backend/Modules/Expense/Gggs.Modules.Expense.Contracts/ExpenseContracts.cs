namespace Gggs.Modules.Expense.Contracts;

/// <summary>A travel advance paid out and not yet settled against claims.</summary>
public sealed record OpenAdvance(Guid TravelRequestId, string Number, decimal Amount, DateOnly PaidOn);

/// <summary>Money an employee still owes or is owed through expenses, for exit settlement (ADR-12).</summary>
public interface IExpenseLedger
{
    Task<IReadOnlyList<OpenAdvance>> OpenAdvancesAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
