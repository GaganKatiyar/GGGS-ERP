using Gggs.Talent.Application.Performance;
using Gggs.Talent.Domain.Performance;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

public sealed class PerformanceCycleRepository(TalentDbContext dbContext) : IPerformanceCycleRepository
{
    public async Task<PerformanceCycle?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.PerformanceCycles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(PerformanceCycle cycle) => dbContext.PerformanceCycles.Add(cycle);
}

public sealed class GoalRepository(TalentDbContext dbContext) : IGoalRepository
{
    public async Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Goals.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Goal goal) => dbContext.Goals.Add(goal);

    public void AddCheckIn(GoalCheckIn checkIn) => dbContext.GoalCheckIns.Add(checkIn);
}

public sealed class ReviewRepository(TalentDbContext dbContext) : IReviewRepository
{
    public async Task<Review?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> ExistsForEmployeeAndCycleAsync(Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => await dbContext.Reviews.AnyAsync(x => x.EmployeeId == employeeId && x.CycleId == cycleId, cancellationToken);

    public void Add(Review review) => dbContext.Reviews.Add(review);
}
