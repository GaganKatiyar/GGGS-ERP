using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Performance;

namespace Gggs.Talent.Application.Performance;

public sealed class GoalService(IGoalRepository goals, IPerformanceCycleRepository cycles, IUnitOfWork unitOfWork)
{
    public async Task<Goal> CreateAsync(
        Guid cycleId, Guid employeeId, string title, string? description, decimal targetValue, string unit,
        DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        PerformanceCycle cycle = await cycles.GetAsync(cycleId, cancellationToken)
            ?? throw new NotFoundException(nameof(PerformanceCycle), cycleId);

        Guard(
            cycle.Status == PerformanceCycleStatus.Open,
            "CYCLE_NOT_OPEN_FOR_GOALS",
            $"Cycle '{cycle.Name}' must be Open to set goals against it; it is {cycle.Status}.");

        Goal goal = Goal.Create(cycleId, employeeId, title, description, targetValue, unit, dueDate);
        goals.Add(goal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return goal;
    }

    public async Task<Goal> CheckInAsync(
        Guid goalId, DateOnly checkInDate, decimal value, string? comment, CancellationToken cancellationToken = default)
    {
        Goal goal = await goals.GetAsync(goalId, cancellationToken)
            ?? throw new NotFoundException(nameof(Goal), goalId);

        goal.ApplyCheckIn(value);
        goals.AddCheckIn(GoalCheckIn.Create(goalId, checkInDate, value, comment));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return goal;
    }

    public async Task<Goal> CancelAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        Goal goal = await goals.GetAsync(goalId, cancellationToken)
            ?? throw new NotFoundException(nameof(Goal), goalId);

        goal.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return goal;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
