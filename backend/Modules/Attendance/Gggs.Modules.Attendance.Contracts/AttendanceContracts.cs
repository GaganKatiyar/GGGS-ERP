namespace Gggs.Modules.Attendance.Contracts;

/// <summary>Judged attendance for payroll (ADR-12).</summary>
public interface IAttendanceLedger
{
    /// <summary>
    /// Days absent per person in the window: an absent day counts 1, a half day 0.5. Leave, weekly
    /// offs and holidays are not absences. People with no absence are left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> AbsenceDaysAsync(IEnumerable<Guid> employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);

    /// <summary>
    /// People absent without leave on at least <paramref name="minimumDays"/> consecutive working days
    /// up to yesterday, after a day they worked: weekly offs and holidays are stepped over, leave or a
    /// worked day ends the run. Someone who never attended in the window is left out, so a tenant that
    /// does not record attendance raises nothing.
    /// </summary>
    Task<IReadOnlyList<AbsenceStreak>> AbsenceStreaksAsync(IEnumerable<Guid> employeeIds, int minimumDays, CancellationToken cancellationToken = default);

    /// <summary>One person's judged days in the window, for showing beside their timesheet. Days not judged yet are left out.</summary>
    Task<IReadOnlyList<AttendedDay>> DaysAsync(Guid employeeId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);
}

/// <summary>A judged day: its status (Present, WeeklyOff...) and the time worked.</summary>
public sealed record AttendedDay(DateOnly Date, string Status, int WorkedMinutes);

/// <summary>An unbroken run of unexplained absence, and the last day the person worked before it.</summary>
public sealed record AbsenceStreak(Guid EmployeeId, DateOnly FirstAbsentDay, DateOnly LastWorkedDay, int WorkingDaysAbsent);
