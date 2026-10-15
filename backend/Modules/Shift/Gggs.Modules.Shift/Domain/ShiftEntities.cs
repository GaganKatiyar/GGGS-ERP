using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Shift.Domain;

/// <summary>
/// A working pattern: when the day starts and ends, how long the break is, how late still counts
/// as on time, how much work makes a half or a full day, and which weekdays are off.
/// </summary>
public sealed class WorkShift : TenantAggregateRoot
{
    private WorkShift()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public int BreakMinutes { get; private set; }

    public int GraceMinutes { get; private set; }

    public int HalfDayMinutes { get; private set; }

    public int FullDayMinutes { get; private set; }

    /// <summary>Days off as a bit set over <see cref="DayOfWeek"/> (bit 0 = Sunday).</summary>
    public int WeeklyOffMask { get; private set; }

    /// <summary>The shift of anyone without an assignment. One per tenant.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Scheduled length, across midnight for a night shift.</summary>
    public int DurationMinutes => Duration(StartTime, EndTime);

    public static WorkShift Create(
        string code,
        string name,
        TimeOnly start,
        TimeOnly end,
        int breakMinutes,
        int graceMinutes,
        int halfDayMinutes,
        int fullDayMinutes,
        IReadOnlyCollection<DayOfWeek> weeklyOff)
    {
        var shift = new WorkShift { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32) };
        shift.Update(name, start, end, breakMinutes, graceMinutes, halfDayMinutes, fullDayMinutes, weeklyOff);
        return shift;
    }

    public void Update(
        string name,
        TimeOnly start,
        TimeOnly end,
        int breakMinutes,
        int graceMinutes,
        int halfDayMinutes,
        int fullDayMinutes,
        IReadOnlyCollection<DayOfWeek> weeklyOff)
    {
        ArgumentNullException.ThrowIfNull(weeklyOff);

        int duration = Duration(start, end);
        if (duration < 60)
        {
            throw new DomainRuleException("SHIFT_TOO_SHORT", "A shift lasts at least an hour.");
        }

        if (breakMinutes < 0 || breakMinutes >= duration)
        {
            throw new DomainRuleException("SHIFT_BREAK", "The break must be shorter than the shift.");
        }

        if (graceMinutes is < 0 or > 240)
        {
            throw new DomainRuleException("SHIFT_GRACE", "Grace is between 0 and 240 minutes.");
        }

        if (halfDayMinutes <= 0 || halfDayMinutes > fullDayMinutes || fullDayMinutes > duration)
        {
            throw new DomainRuleException("SHIFT_THRESHOLDS", "Half-day work must be positive and no more than a full day, which fits within the shift.");
        }

        if (weeklyOff.Count >= 7)
        {
            throw new DomainRuleException("SHIFT_NO_WORKING_DAY", "A shift needs at least one working day.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        StartTime = start;
        EndTime = end;
        BreakMinutes = breakMinutes;
        GraceMinutes = graceMinutes;
        HalfDayMinutes = halfDayMinutes;
        FullDayMinutes = fullDayMinutes;
        WeeklyOffMask = weeklyOff.Aggregate(0, (mask, day) => mask | (1 << (int)day));
    }

    public bool IsOff(DayOfWeek day) => (WeeklyOffMask & (1 << (int)day)) != 0;

    public IReadOnlyList<DayOfWeek> WeeklyOffDays => [.. Enum.GetValues<DayOfWeek>().Where(IsOff)];

    public void MakeDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsDefault)
        {
            throw new DomainRuleException("SHIFT_DEFAULT", "The default shift cannot be deactivated; make another shift the default first.");
        }

        IsActive = false;
    }

    public static int Duration(TimeOnly start, TimeOnly end)
    {
        int minutes = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return minutes > 0 ? minutes : minutes + (24 * 60);
    }
}

/// <summary>
/// "From this date (until that date) this person works that shift." An employee's assignments form
/// a timeline without overlaps; assigning over a stretch trims or splits whatever was there.
/// </summary>
public sealed class ShiftAssignment : TenantAggregateRoot
{
    private ShiftAssignment()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid ShiftId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last day covered; null runs on.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public static ShiftAssignment Create(Guid employeeId, Guid shiftId, DateOnly from, DateOnly? to)
    {
        if (to is { } end && end < from)
        {
            throw new DomainRuleException("SHIFT_ASSIGNMENT_DATES", "An assignment cannot end before it starts.");
        }

        return new ShiftAssignment { EmployeeId = Guard.NotEmpty(employeeId), ShiftId = Guard.NotEmpty(shiftId), EffectiveFrom = from, EffectiveTo = to };
    }

    public bool Covers(DateOnly day) => day >= EffectiveFrom && (EffectiveTo is null || day <= EffectiveTo);

    public bool Overlaps(DateOnly from, DateOnly? to)
        => EffectiveFrom <= (to ?? DateOnly.MaxValue) && (EffectiveTo ?? DateOnly.MaxValue) >= from;

    public void EndOn(DateOnly lastDay) => EffectiveTo = lastDay;

    public void StartOn(DateOnly firstDay) => EffectiveFrom = firstDay;
}

/// <summary>One day different from the pattern: another shift, or a day off (no shift).</summary>
public sealed class RosterOverride : TenantAggregateRoot
{
    private RosterOverride()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Null makes the day off.</summary>
    public Guid? ShiftId { get; private set; }

    public string? Reason { get; private set; }

    public static RosterOverride Create(Guid employeeId, DateOnly date, Guid? shiftId, string? reason) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        Date = date,
        ShiftId = shiftId,
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300),
    };

    public void Change(Guid? shiftId, string? reason)
    {
        ShiftId = shiftId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300);
    }
}

public enum SwapStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>Two people trade their shifts for one day, once approved.</summary>
public sealed class ShiftSwapRequest : TenantAggregateRoot
{
    private ShiftSwapRequest()
    {
    }

    public Guid RequesterEmployeeId { get; private set; }

    public Guid CounterpartEmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public string? Reason { get; private set; }

    public SwapStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public static ShiftSwapRequest Create(Guid requester, Guid counterpart, DateOnly date, string? reason)
    {
        if (requester == counterpart)
        {
            throw new DomainRuleException("SHIFT_SWAP_SELF", "Choose someone else to swap with.");
        }

        return new ShiftSwapRequest
        {
            RequesterEmployeeId = Guard.NotEmpty(requester),
            CounterpartEmployeeId = Guard.NotEmpty(counterpart),
            Date = date,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300),
            Status = SwapStatus.Pending,
        };
    }

    public void AwaitApproval(Guid instanceId) => WorkflowInstanceId = instanceId;

    public void Approve() => Move(SwapStatus.Approved);

    public void Reject() => Move(SwapStatus.Rejected);

    public void Cancel() => Move(SwapStatus.Cancelled);

    private void Move(SwapStatus status)
    {
        if (Status != SwapStatus.Pending)
        {
            throw new DomainRuleException("SHIFT_SWAP_CLOSED", $"This swap is already {Status}.");
        }

        Status = status;
    }
}
