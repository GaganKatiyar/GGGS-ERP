using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Performance;

public enum GoalStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// One thing an employee is working toward in a cycle - "Ship the v2 API," "Reduce support
/// response time to under 4 hours." No KPI library reference, cascading parent goal, or weighting
/// in this v1 slice (architecture.md's <c>Goal.KpiId</c>/<c>ParentGoalId</c>/<c>Weight</c>): a goal
/// is free text plus a numeric target/current value pair, and <see cref="GoalCheckIn"/> is how
/// progress against it gets recorded over time.
/// </summary>
public sealed class Goal : TenantAggregateRoot
{
    private Goal()
    {
        Title = null!;
        Unit = null!;
    }

    public Guid CycleId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public decimal TargetValue { get; private set; }

    public decimal CurrentValue { get; private set; }

    public string Unit { get; private set; }

    public DateOnly DueDate { get; private set; }

    public GoalStatus Status { get; private set; }

    public static Goal Create(
        Guid cycleId, Guid employeeId, string title, string? description, decimal targetValue, string unit, DateOnly dueDate)
    {
        Guard.Against(targetValue <= 0, "GOAL_TARGET_MUST_BE_POSITIVE", "The target value must be greater than zero.");

        return new Goal
        {
            CycleId = Guard.NotEmpty(cycleId),
            EmployeeId = Guard.NotEmpty(employeeId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 1000),
            TargetValue = targetValue,
            CurrentValue = 0,
            Unit = Guard.MaxLength(Guard.NotNullOrWhiteSpace(unit).Trim(), 30),
            DueDate = dueDate,
            Status = GoalStatus.Active,
            IsActive = true,
        };
    }

    /// <summary>Records progress - the goal's own <see cref="CurrentValue"/> is a cache of the
    /// latest check-in's value, not summed across check-ins, since a check-in reports "where things
    /// stand now," not an increment.</summary>
    public void ApplyCheckIn(decimal value)
    {
        Guard.Against(
            Status != GoalStatus.Active, "GOAL_NOT_ACTIVE", $"Goal '{Title}' must be Active to check in against it; it is {Status}.");

        CurrentValue = value;

        if (CurrentValue >= TargetValue)
        {
            Status = GoalStatus.Completed;
        }
    }

    public void Cancel()
    {
        Guard.Against(
            Status != GoalStatus.Active, "GOAL_NOT_ACTIVE", $"Goal '{Title}' must be Active to cancel; it is {Status}.");

        Status = GoalStatus.Cancelled;
    }
}

/// <summary>One progress update against a <see cref="Goal"/> - append-only, the same "the ledger is
/// the source of truth, the parent's own field is a derived cache" shape <c>LeaveLedgerEntry</c>
/// and <c>LeaveBalance</c> already use, scaled down to a single running value instead of a
/// debit/credit sum.</summary>
public sealed class GoalCheckIn : TenantAggregateRoot
{
    private GoalCheckIn()
    {
    }

    public Guid GoalId { get; private set; }

    public DateOnly CheckInDate { get; private set; }

    public decimal Value { get; private set; }

    public string? Comment { get; private set; }

    public static GoalCheckIn Create(Guid goalId, DateOnly checkInDate, decimal value, string? comment) => new()
    {
        GoalId = Guard.NotEmpty(goalId),
        CheckInDate = checkInDate,
        Value = value,
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 500),
        IsActive = true,
    };
}
