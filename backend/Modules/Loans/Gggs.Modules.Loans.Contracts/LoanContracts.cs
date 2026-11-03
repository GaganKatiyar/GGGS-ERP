namespace Gggs.Modules.Loans.Contracts;

/// <summary>What is still owed on one loan on a day: unpaid principal plus interest accrued since the last instalment.</summary>
public sealed record LoanOutstanding(Guid LoanId, string Number, string TypeName, decimal Principal, decimal Interest)
{
    public decimal Total => Principal + Interest;
}

/// <summary>
/// Employee loans for a leaver's full and final settlement (Separation, ADR-12): what is owed, and
/// handing the loans over so the settlement recovers them instead of monthly payroll.
/// </summary>
public interface ILoanLedger
{
    /// <summary>The employee's loans still being repaid, valued on <paramref name="onDay"/>.</summary>
    Task<IReadOnlyList<LoanOutstanding>> OutstandingAsync(Guid employeeId, DateOnly onDay, CancellationToken cancellationToken = default);

    /// <summary>
    /// The settlement takes over recovery: instalments not yet deducted are withdrawn from payroll
    /// and stop being raised. Returns how many loans were handed over.
    /// </summary>
    Task<int> HandOverToSettlementAsync(Guid employeeId, Guid settlementId, CancellationToken cancellationToken = default);

    /// <summary>The settlement was reopened: its loans go back to monthly recovery.</summary>
    Task<int> TakeBackFromSettlementAsync(Guid settlementId, CancellationToken cancellationToken = default);

    /// <summary>The settlement was paid: the amounts it recovered close its loans.</summary>
    Task<int> SettledAsync(Guid settlementId, IReadOnlyDictionary<Guid, decimal> recovered, DateOnly paidOn, string? reference, CancellationToken cancellationToken = default);
}
