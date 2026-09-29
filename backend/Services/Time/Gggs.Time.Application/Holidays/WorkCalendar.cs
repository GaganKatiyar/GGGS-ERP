using Gggs.Time.Domain.Holidays;
using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Holidays;

public sealed class WorkCalendar(IHolidayRepository holidays) : IWorkCalendar
{
    public async Task<IReadOnlyList<(DateOnly Date, decimal DayFraction)>> GetWorkDaysAsync(
        Guid employeeId,
        DateOnly fromDate,
        LeaveDaySession fromSession,
        DateOnly toDate,
        LeaveDaySession toSession,
        CancellationToken cancellationToken = default)
    {
        var holidayDates = new HashSet<DateOnly>();

        for (int year = fromDate.Year; year <= toDate.Year; year++)
        {
            HolidayCalendar? calendar = await holidays.GetForYearAsync(year, cancellationToken);

            if (calendar is null)
            {
                continue;
            }

            IReadOnlyList<Holiday> yearHolidays = await holidays.GetHolidaysAsync(calendar.Id, cancellationToken);

            foreach (Holiday holiday in yearHolidays.Where(h => !h.IsOptional))
            {
                holidayDates.Add(holiday.HolidayDate);
            }
        }

        var days = new List<(DateOnly Date, decimal DayFraction)>();
        bool isSingleDate = fromDate == toDate;

        for (DateOnly date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (holidayDates.Contains(date))
            {
                continue;
            }

            // A single-date request spans both sessions at once (e.g. First to Second covers the
            // whole day); a multi-date request's half-day sessions apply only at its own boundary.
            bool isHalfDay = isSingleDate
                ? fromSession != LeaveDaySession.Full || toSession != LeaveDaySession.Full
                : (date == fromDate && fromSession != LeaveDaySession.Full)
                    || (date == toDate && toSession != LeaveDaySession.Full);

            decimal fraction = isHalfDay ? 0.5m : 1m;

            days.Add((date, fraction));
        }

        return days;
    }
}
