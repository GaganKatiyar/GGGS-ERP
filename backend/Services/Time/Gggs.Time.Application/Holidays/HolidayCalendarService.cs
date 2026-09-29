using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Holidays;

namespace Gggs.Time.Application.Holidays;

/// <summary>Administering a tenant's own holiday calendars.</summary>
public sealed class HolidayCalendarService(IHolidayRepository holidays, IUnitOfWork unitOfWork)
{
    public async Task<HolidayCalendar> CreateAsync(
        string name, int year, string countryCode, string? stateCode, CancellationToken cancellationToken = default)
    {
        HolidayCalendar? existing = await holidays.GetForYearAsync(year, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException("HOLIDAY_CALENDAR_YEAR_EXISTS", $"A holiday calendar for {year} already exists.");
        }

        HolidayCalendar calendar = HolidayCalendar.Create(name, year, countryCode, stateCode);
        holidays.Add(calendar);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return calendar;
    }

    public async Task<Holiday> AddHolidayAsync(
        Guid calendarId, DateOnly holidayDate, string name, bool isOptional, bool isRestricted, CancellationToken cancellationToken = default)
    {
        _ = await holidays.GetAsync(calendarId, cancellationToken)
            ?? throw new NotFoundException(nameof(HolidayCalendar), calendarId);

        if (await holidays.HolidayExistsAsync(calendarId, holidayDate, cancellationToken))
        {
            throw new DomainRuleException(
                "HOLIDAY_ALREADY_EXISTS", $"This calendar already has a holiday on {holidayDate:yyyy-MM-dd}.");
        }

        Holiday holiday = Holiday.Create(calendarId, holidayDate, name, isOptional, isRestricted);
        holidays.Add(holiday);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return holiday;
    }
}
