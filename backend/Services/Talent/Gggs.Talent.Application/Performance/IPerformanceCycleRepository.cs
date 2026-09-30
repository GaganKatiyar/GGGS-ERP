using Gggs.Talent.Domain.Performance;

namespace Gggs.Talent.Application.Performance;

public interface IPerformanceCycleRepository
{
    Task<PerformanceCycle?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(PerformanceCycle cycle);
}

public interface IGoalRepository
{
    Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Goal goal);

    void AddCheckIn(GoalCheckIn checkIn);
}

public interface IReviewRepository
{
    Task<Review?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForEmployeeAndCycleAsync(Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    void Add(Review review);
}
