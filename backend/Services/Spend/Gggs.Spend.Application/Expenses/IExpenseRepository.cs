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

    void Add(ExpenseClaim claim);
}

public interface IExpenseLineRepository
{
    Task<IReadOnlyList<ExpenseLine>> ListForClaimAsync(Guid claimId, CancellationToken cancellationToken = default);

    void Add(ExpenseLine line);
}
