using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

/// <summary>
/// A tenant-defined work shift: the timing template <see cref="ShiftRoster"/> assigns to employees
/// per day, and <see cref="AttendanceDay"/> computation reads to judge lateness and full/half-day
/// thresholds.
/// </summary>
public sealed class Shift : TenantAggregateRoot
{
    private Shift()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public int BreakMinutes { get; private set; }

    /// <summary>True when <see cref="EndTime"/> is on the calendar day after <see cref="StartTime"/>
    /// - a shift starting at 22:00 and ending at 06:00 crosses midnight, which changes how
    /// <c>AttendanceDayProcessor</c> matches punches to the shift's own date.</summary>
    public bool IsNightShift { get; private set; }

    /// <summary>Minutes after <see cref="StartTime"/> a first-in punch is still on time.</summary>
    public int GraceInMinutes { get; private set; }

    /// <summary>Minutes before <see cref="EndTime"/> a last-out punch still counts as a full
    /// departure, not an early leave.</summary>
    public int GraceOutMinutes { get; private set; }

    /// <summary>Worked minutes at or above this mark counts as a full day present.</summary>
    public int FullDayMinMinutes { get; private set; }

    /// <summary>Worked minutes at or above this mark (but below <see cref="FullDayMinMinutes"/>)
    /// counts as a half day rather than absent.</summary>
    public int HalfDayMinMinutes { get; private set; }

    public static Shift Create(
        string code,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        int breakMinutes,
        bool isNightShift,
        int graceInMinutes,
        int graceOutMinutes,
        int fullDayMinMinutes,
        int halfDayMinMinutes)
    {
        Guard.Against(breakMinutes < 0, "SHIFT_INVALID_BREAK_MINUTES", "Break minutes cannot be negative.");
        Guard.Against(graceInMinutes < 0, "SHIFT_INVALID_GRACE_MINUTES", "Grace-in minutes cannot be negative.");
        Guard.Against(graceOutMinutes < 0, "SHIFT_INVALID_GRACE_MINUTES", "Grace-out minutes cannot be negative.");
        Guard.Against(fullDayMinMinutes <= 0, "SHIFT_INVALID_THRESHOLD", "Full-day minimum minutes must be positive.");
        Guard.Against(
            halfDayMinMinutes <= 0 || halfDayMinMinutes >= fullDayMinMinutes,
            "SHIFT_INVALID_THRESHOLD",
            "Half-day minimum minutes must be positive and less than the full-day minimum.");

        return new Shift
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            StartTime = startTime,
            EndTime = endTime,
            BreakMinutes = breakMinutes,
            IsNightShift = isNightShift,
            GraceInMinutes = graceInMinutes,
            GraceOutMinutes = graceOutMinutes,
            FullDayMinMinutes = fullDayMinMinutes,
            HalfDayMinMinutes = halfDayMinMinutes,
            IsActive = true,
        };
    }

    /// <summary>The shift's scheduled worked minutes, net of its own break - what
    /// <see cref="FullDayMinMinutes"/>/<see cref="HalfDayMinMinutes"/> are judged against.</summary>
    public int ScheduledMinutes()
    {
        int totalMinutes = (int)(EndTime.ToTimeSpan() - StartTime.ToTimeSpan()).TotalMinutes;

        if (totalMinutes < 0)
        {
            // A night shift's EndTime is "earlier" than StartTime on the clock because it falls on
            // the next calendar day - add a day back to get the real span.
            totalMinutes += 24 * 60;
        }

        return Math.Max(0, totalMinutes - BreakMinutes);
    }
}
