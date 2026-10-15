using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.Shift.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

/// <summary>
/// Holidays and approved leave by day, and which days are working days for someone - their shift's
/// weekly offs and their location's holidays taken out.
/// </summary>
internal sealed class LeaveCalendarService(AppDbContext dbContext, IEmployeeDirectory employees, IShiftSchedule schedule) : ILeaveCalendar
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>>> GetHolidaysAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        List<HolidayCalendar> calendars = await dbContext.HolidayCalendars.AsNoTracking().ToListAsync(cancellationToken);
        List<Holiday> holidays = await dbContext.Holidays.AsNoTracking()
            .Where(holiday => holiday.Date >= firstDay && holiday.Date <= lastDay)
            .ToListAsync(cancellationToken);

        IReadOnlyList<EmployeeSummary> people = await employees.GetManyAsync(employeeIds, cancellationToken);
        var result = new Dictionary<Guid, IReadOnlyList<HolidayEntry>>();

        foreach (EmployeeSummary person in people)
        {
            HolidayCalendar? calendar = CalendarFor(calendars, person.LocationId);
            result[person.Id] = calendar is null
                ? []
                : [.. holidays.Where(holiday => holiday.CalendarId == calendar.Id).OrderBy(holiday => holiday.Date).Select(holiday => new HolidayEntry(holiday.Date, holiday.Name, holiday.IsOptional))];
        }

        return result;
    }

    public async Task<IReadOnlyList<LeaveDay>> GetApprovedLeaveAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];

        var requests = await dbContext.LeaveRequests.AsNoTracking()
            .Where(request => ids.Contains(request.EmployeeId)
                && request.Status == LeaveRequestStatus.Approved
                && request.StartDate <= lastDay
                && request.EndDate >= firstDay)
            .Join(dbContext.LeaveTypes.AsNoTracking(), request => request.LeaveTypeId, type => type.Id, (request, type) => new { request, type })
            .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return [];
        }

        Dictionary<(Guid, DateOnly), bool> working = await WorkingDaysAsync(ids, firstDay, lastDay, cancellationToken);
        var days = new List<LeaveDay>();

        foreach (var row in requests)
        {
            for (DateOnly day = Max(row.request.StartDate, firstDay); day <= Min(row.request.EndDate, lastDay); day = day.AddDays(1))
            {
                if (!working.GetValueOrDefault((row.request.EmployeeId, day), true))
                {
                    continue;
                }

                bool half = (day == row.request.StartDate && row.request.StartsSecondHalf) || (day == row.request.EndDate && row.request.EndsFirstHalf);
                days.Add(new LeaveDay(row.request.EmployeeId, day, row.type.Code, row.type.Name, half, row.type.IsPaid));
            }
        }

        return days;
    }

    /// <summary>For each employee and day: is it a working day (scheduled, not a weekly off, not a holiday)?</summary>
    public async Task<Dictionary<(Guid EmployeeId, DateOnly Day), bool>> WorkingDaysAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduledDay> scheduled = await schedule.GetAsync(employeeIds, firstDay, lastDay, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = await GetHolidaysAsync(employeeIds, firstDay, lastDay, cancellationToken);

        var result = new Dictionary<(Guid, DateOnly), bool>();
        foreach (ScheduledDay day in scheduled)
        {
            bool holiday = holidays.TryGetValue(day.EmployeeId, out IReadOnlyList<HolidayEntry>? list)
                && list.Any(entry => entry.Date == day.Date && !entry.IsOptional);
            result[(day.EmployeeId, day.Date)] = !day.IsWeeklyOff && !holiday;
        }

        return result;
    }

    private static HolidayCalendar? CalendarFor(List<HolidayCalendar> calendars, Guid? locationId)
        => (locationId is { } location ? calendars.FirstOrDefault(calendar => calendar.LocationIds.Contains(location)) : null)
            ?? calendars.FirstOrDefault(calendar => calendar.IsDefault);

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
