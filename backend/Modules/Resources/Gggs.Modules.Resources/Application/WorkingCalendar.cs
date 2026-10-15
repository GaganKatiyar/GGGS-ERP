using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Shift.Contracts;

namespace Gggs.Modules.Resources.Application;

/// <summary>One person's working time over a window: the days they are rostered to work, and leave taken on them.</summary>
internal sealed record PersonCalendar(IReadOnlyCollection<DateOnly> WorkingDays, IReadOnlyDictionary<DateOnly, decimal> LeaveFractions)
{
    public decimal LeaveDays => LeaveFractions.Values.Sum();

    /// <summary>Working days left after leave, in days.</summary>
    public decimal AvailableDays(DateOnly from, DateOnly to)
        => WorkingDays.Count(day => day >= from && day <= to) - LeaveFractions.Where(pair => pair.Key >= from && pair.Key <= to).Sum(pair => pair.Value);
}

/// <summary>
/// Working days per person from their shift roster, less public holidays, with approved leave by day -
/// what capacity and utilization are measured against.
/// </summary>
internal sealed class WorkingCalendar(IShiftSchedule shifts, ILeaveCalendar leave)
{
    public async Task<IReadOnlyDictionary<Guid, PersonCalendar>> ForAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, PersonCalendar>();
        }

        IReadOnlyList<ScheduledDay> schedule = await shifts.GetAsync(employeeIds, from, to, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = await leave.GetHolidaysAsync(employeeIds, from, to, cancellationToken);
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync(employeeIds, from, to, cancellationToken);

        ILookup<Guid, ScheduledDay> byEmployee = schedule.ToLookup(day => day.EmployeeId);
        ILookup<Guid, LeaveDay> leaveByEmployee = leaveDays.ToLookup(day => day.EmployeeId);

        var result = new Dictionary<Guid, PersonCalendar>();
        foreach (Guid employeeId in employeeIds)
        {
            HashSet<DateOnly> off = [.. (holidays.GetValueOrDefault(employeeId) ?? []).Where(holiday => !holiday.IsOptional).Select(holiday => holiday.Date)];
            HashSet<DateOnly> working = [.. byEmployee[employeeId].Where(day => !day.IsWeeklyOff && !off.Contains(day.Date)).Select(day => day.Date)];
            Dictionary<DateOnly, decimal> leaveFractions = leaveByEmployee[employeeId]
                .Where(day => working.Contains(day.Date))
                .GroupBy(day => day.Date)
                .ToDictionary(group => group.Key, group => Math.Min(1m, group.Sum(day => day.IsHalfDay ? 0.5m : 1m)));
            result[employeeId] = new PersonCalendar(working, leaveFractions);
        }

        return result;
    }

    /// <summary>The months touching a window, each clipped to it.</summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> Months(DateOnly from, DateOnly to)
    {
        List<(DateOnly, DateOnly)> months = [];
        var start = new DateOnly(from.Year, from.Month, 1);
        while (start <= to)
        {
            DateOnly end = start.AddMonths(1).AddDays(-1);
            months.Add((start < from ? from : start, end > to ? to : end));
            start = start.AddMonths(1);
        }

        return months;
    }
}
