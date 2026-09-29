using Gggs.Time.Domain.Holidays;

namespace Gggs.Time.Application.Holidays;

public interface IHolidayRepository
{
    Task<HolidayCalendar?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The one calendar assigned tenant-wide for <paramref name="year"/> - v1 has no
    /// per-branch/per-location calendar assignment (architecture.md §7.2's
    /// <c>HolidayCalendarAssignment</c>), so every employee shares the same calendar for a given
    /// year.</summary>
    Task<HolidayCalendar?> GetForYearAsync(int year, CancellationToken cancellationToken = default);

    void Add(HolidayCalendar calendar);

    Task<bool> HolidayExistsAsync(Guid holidayCalendarId, DateOnly holidayDate, CancellationToken cancellationToken = default);

    void Add(Holiday holiday);

    Task<IReadOnlyList<Holiday>> GetHolidaysAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default);
}
