using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Performance;

public enum PerformanceCycleStatus
{
    Draft = 1,
    Open = 2,
    Closed = 3,
}

/// <summary>
/// A review period - "H1 2026," "Annual 2026." Goals and reviews are both set against a cycle. No
/// tenant-configurable phases (goal-setting window, self-review window, manager-review window,
/// calibration window - architecture.md's <c>PerformanceCycle.PhasesJson</c>) or review template in
/// this v1 slice: a cycle is either open for goal-setting and reviews, or it is not, matching every
/// other v1 module this codebase has scoped down to a single flag ahead of a real multi-phase or
/// multi-target configuration model nothing yet needs (Leave v1's single tenant-wide policy instead
/// of <c>PolicyAssignment</c> targeting; Onboarding v1's hand-picked template instead of
/// <c>AppliesToJson</c>).
/// </summary>
public sealed class PerformanceCycle : TenantAggregateRoot
{
    private PerformanceCycle()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public PerformanceCycleStatus Status { get; private set; }

    public static PerformanceCycle Create(string name, DateOnly startDate, DateOnly endDate)
    {
        Guard.Against(endDate < startDate, "CYCLE_END_BEFORE_START", "The cycle cannot end before it starts.");

        return new PerformanceCycle
        {
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
            StartDate = startDate,
            EndDate = endDate,
            Status = PerformanceCycleStatus.Draft,
            IsActive = true,
        };
    }

    /// <summary>Draft -> Open: goals may now be set and reviews raised against this cycle.</summary>
    public void Open()
    {
        Guard.Against(
            Status != PerformanceCycleStatus.Draft, "CYCLE_NOT_DRAFT", $"Cycle '{Name}' must be Draft to open; it is {Status}.");

        Status = PerformanceCycleStatus.Open;
    }

    /// <summary>Open -> Closed: terminal. Goals and reviews already raised keep their own state;
    /// closing the cycle only stops new ones from being started against it.</summary>
    public void Close()
    {
        Guard.Against(
            Status != PerformanceCycleStatus.Open, "CYCLE_NOT_OPEN", $"Cycle '{Name}' must be Open to close; it is {Status}.");

        Status = PerformanceCycleStatus.Closed;
    }
}
