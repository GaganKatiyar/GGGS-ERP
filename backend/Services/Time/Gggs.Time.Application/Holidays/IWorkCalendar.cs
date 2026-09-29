using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Holidays;

/// <summary>
/// Turns a date range into the working days within it, for one employee. v1 counts every day that
/// is not a public holiday on the tenant's default calendar as a working day - no per-employee
/// shift/roster/weekly-off pattern yet (that is Attendance's own shift/roster tables, not built in
/// this chunk), so Saturdays and Sundays currently count as working days unless a holiday falls on
/// them. <see cref="LeaveRequestService"/> is written against this interface rather than the
/// holiday tables directly so that gap can close later without touching request submission itself.
/// </summary>
public interface IWorkCalendar
{
    /// <summary>Every date from <paramref name="fromDate"/> to <paramref name="toDate"/> inclusive
    /// that counts as a working day, each paired with the fraction of a day
    /// <paramref name="fromSession"/>/<paramref name="toSession"/> imply for its boundary dates
    /// (0.5 for a half-day start/end, 1.0 otherwise; a single-date request combines both
    /// sessions).</summary>
    Task<IReadOnlyList<(DateOnly Date, decimal DayFraction)>> GetWorkDaysAsync(
        Guid employeeId,
        DateOnly fromDate,
        LeaveDaySession fromSession,
        DateOnly toDate,
        LeaveDaySession toSession,
        CancellationToken cancellationToken = default);
}
