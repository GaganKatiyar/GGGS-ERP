using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Application.Travel;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;
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

    public async Task<IReadOnlyList<ExpenseClaim>> ListApprovedForTravelRequestAsync(
        Guid travelRequestId, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseClaims
            .Where(x => x.TravelRequestId == travelRequestId && x.Status == ExpenseClaimStatus.Approved)
            .ToListAsync(cancellationToken);

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

public sealed class TravelRequestRepository(SpendDbContext dbContext) : ITravelRequestRepository
{
    public async Task<TravelRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.TravelRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> RequestNoExistsAsync(string requestNo, CancellationToken cancellationToken = default)
        => await dbContext.TravelRequests.AnyAsync(x => x.RequestNo == requestNo, cancellationToken);

    public void Add(TravelRequest request) => dbContext.TravelRequests.Add(request);
}

public sealed class TravelAdvanceRepository(SpendDbContext dbContext) : ITravelAdvanceRepository
{
    public async Task<TravelAdvance?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.TravelAdvances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TravelAdvance>> ListForRequestAsync(Guid travelRequestId, CancellationToken cancellationToken = default)
        => await dbContext.TravelAdvances.Where(x => x.TravelRequestId == travelRequestId).ToListAsync(cancellationToken);

    public void Add(TravelAdvance advance) => dbContext.TravelAdvances.Add(advance);
}

public sealed class TravelSettlementRepository(SpendDbContext dbContext) : ITravelSettlementRepository
{
    public async Task<TravelSettlement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.TravelSettlements.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<TravelSettlement?> GetForRequestAsync(Guid travelRequestId, CancellationToken cancellationToken = default)
        => await dbContext.TravelSettlements.FirstOrDefaultAsync(x => x.TravelRequestId == travelRequestId, cancellationToken);

    public void Add(TravelSettlement settlement) => dbContext.TravelSettlements.Add(settlement);

    public void Remove(TravelSettlement settlement) => dbContext.TravelSettlements.Remove(settlement);
}
