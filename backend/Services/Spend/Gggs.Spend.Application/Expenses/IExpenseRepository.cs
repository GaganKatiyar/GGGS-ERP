using Gggs.Spend.Domain.Expenses;

namespace Gggs.Spend.Application.Expenses;

public interface IExpenseCategoryRepository
{
    Task<ExpenseCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(ExpenseCategory category);
}

public interface IExpenseClaimRepository
{
    Task<ExpenseClaim?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ClaimNoExistsAsync(string claimNo, CancellationToken cancellationToken = default);

    /// <summary>Every Approved claim linked to one trip - what
    /// <c>Travel.TravelSettlementService</c> sums for <see cref="ExpenseClaim.Total"/> to reconcile
    /// against the trip's advance. Only Approved claims count: a still-Submitted or Rejected claim
    /// is not yet (or never) real spend against the trip.</summary>
    Task<IReadOnlyList<ExpenseClaim>> ListApprovedForTravelRequestAsync(Guid travelRequestId, CancellationToken cancellationToken = default);

    void Add(ExpenseClaim claim);
}

public interface IExpenseLineRepository
{
    Task<IReadOnlyList<ExpenseLine>> ListForClaimAsync(Guid claimId, CancellationToken cancellationToken = default);

    void Add(ExpenseLine line);
}
