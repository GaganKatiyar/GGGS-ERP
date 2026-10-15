namespace Gggs.Modules.Shift.Contracts;

/// <summary>
/// One employee's working day as scheduled: the shift, its timings and thresholds, or a day off.
/// Attendance judges a day against this; Leave uses it to know which days a request actually takes.
/// </summary>
public sealed record ScheduledDay(
    Guid EmployeeId,
    DateOnly Date,
    Guid? ShiftId,
    string ShiftName,
    TimeOnly Start,
    TimeOnly End,
    int BreakMinutes,
    int GraceMinutes,
    int HalfDayMinutes,
    int FullDayMinutes,
    bool IsWeeklyOff)
{
    /// <summary>The shift ends on the next calendar day (a night shift).</summary>
    public bool IsOvernight => End <= Start;
}

/// <summary>The roster other modules read (ADR-12): resolved per day, assignments and overrides applied.</summary>
public interface IShiftSchedule
{
    Task<IReadOnlyList<ScheduledDay>> GetAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);
}
