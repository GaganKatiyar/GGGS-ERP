using Gggs.Time.Application.Holidays;
using Gggs.Time.Application.Leave;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Holidays;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Rebuilds one employee's <see cref="AttendanceDay"/> for one date from its own sources - the
/// roster, that date's valid punches, the holiday calendar, and any approved leave covering the
/// date. Called synchronously after every punch (v1 has no batch job: see architecture.md §22's
/// "large team" data-volume plan for when a nightly job would replace this), so a caller reading
/// <see cref="AttendanceDay"/> right after a punch always sees it reflected.
/// <para>
/// v1 treats <see cref="AttendanceDay.AttendanceDate"/> as a UTC calendar date - the same
/// simplification <c>WorkCalendar</c> and <c>Holiday.HolidayDate</c> already make for leave, rather
/// than introducing a per-employee/tenant time zone lookup this module does not yet need.
/// </para>
/// </summary>
public sealed class AttendanceDayProcessor(
    IShiftRosterRepository rosters,
    IShiftRepository shifts,
    IAttendancePunchRepository punches,
    IAttendanceDayRepository days,
    IHolidayRepository holidays,
    ILeaveRequestRepository leaveRequests)
{
    public async Task<AttendanceDay> RecomputeAsync(Guid employeeId, DateOnly attendanceDate, CancellationToken cancellationToken = default)
    {
        AttendanceDay? day = await days.GetAsync(employeeId, attendanceDate, cancellationToken);

        if (day is null)
        {
            day = AttendanceDay.CreateEmpty(employeeId, attendanceDate);
            days.Add(day);
        }

        ShiftRoster? roster = await rosters.GetForDateAsync(employeeId, attendanceDate, cancellationToken);
        Shift? shift = roster?.ShiftId is { } shiftId ? await shifts.GetAsync(shiftId, cancellationToken) : null;
        bool isWeeklyOff = roster?.IsWeeklyOff ?? false;

        bool isHoliday = false;
        HolidayCalendar? calendar = await holidays.GetForYearAsync(attendanceDate.Year, cancellationToken);

        if (calendar is not null)
        {
            IReadOnlyList<Holiday> yearHolidays = await holidays.GetHolidaysAsync(calendar.Id, cancellationToken);
            isHoliday = yearHolidays.Any(h => h.HolidayDate == attendanceDate && !h.IsOptional);
        }

        Guid? approvedLeaveRequestId = await leaveRequests.GetApprovedRequestIdForDateAsync(employeeId, attendanceDate, cancellationToken);

        DateTime windowStartUtc = attendanceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime windowEndUtc = windowStartUtc.AddDays(1);

        IReadOnlyList<AttendancePunch> punchesForDate =
            await punches.GetForDateAsync(employeeId, windowStartUtc, windowEndUtc, cancellationToken);

        day.Recompute(roster?.ShiftId, shift, isWeeklyOff, isHoliday, approvedLeaveRequestId, punchesForDate);

        return day;
    }
}
