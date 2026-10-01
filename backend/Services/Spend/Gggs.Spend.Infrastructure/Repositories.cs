using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Spend.Infrastructure;

public sealed class ExpenseCategoryRepository(SpendDbContext dbContext) : IExpenseCategoryRepository
{
    public async Task<ExpenseCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseCategories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.ExpenseCategories.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(ExpenseCategory category) => dbContext.ExpenseCategories.Add(category);
}

public sealed class ExpenseClaimRepository(SpendDbContext dbContext) : IExpenseClaimRepository
{
    public async Task<ExpenseClaim?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseClaims.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> ClaimNoExistsAsync(string claimNo, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseClaims.AnyAsync(x => x.ClaimNo == claimNo, cancellationToken);

    public void Add(ExpenseClaim claim) => dbContext.ExpenseClaims.Add(claim);
}

public sealed class ExpenseLineRepository(SpendDbContext dbContext) : IExpenseLineRepository
{
    public async Task<IReadOnlyList<ExpenseLine>> ListForClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseLines.Where(x => x.ClaimId == claimId).ToListAsync(cancellationToken);

    public void Add(ExpenseLine line) => dbContext.ExpenseLines.Add(line);
}

public sealed class EmployeeSnapshotRepository(SpendDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
