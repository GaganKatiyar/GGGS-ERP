using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Holidays;

/// <summary>
/// One year's set of public holidays for one country/state combination. A new one is created per
/// year rather than the same calendar reused with rolling dates, since public holidays genuinely
/// change year to year (a different weekday, a shifted observance).
/// <para>
/// Holds no in-memory collection of its own <see cref="Holiday"/> rows: the two are separate
/// aggregate roots linked by <see cref="Holiday.HolidayCalendarId"/>, the same convention as
/// <see cref="Leave.LeaveRequest"/>/<see cref="Leave.LeaveRequestDay"/> - see that type's own
/// remarks on why. <see cref="HolidayCalendarService.AddHolidayAsync"/> checks for a duplicate date
/// with a repository query rather than an aggregate-held list.
/// </para>
/// </summary>
public sealed class HolidayCalendar : TenantAggregateRoot
{
    private HolidayCalendar()
    {
        Name = null!;
        CountryCode = null!;
    }

    public string Name { get; private set; }

    public int Year { get; private set; }

    public string CountryCode { get; private set; }

    public string? StateCode { get; private set; }

    public static HolidayCalendar Create(string name, int year, string countryCode, string? stateCode) => new()
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
        Year = year,
        CountryCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant(), 2),
        StateCode = string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim().ToUpperInvariant(),
        IsActive = true,
    };
}

public sealed class Holiday : TenantAggregateRoot
{
    private Holiday()
    {
        Name = null!;
    }

    public Guid HolidayCalendarId { get; private set; }

    public DateOnly HolidayDate { get; private set; }

    public string Name { get; private set; }

    /// <summary>Observed by choice, not counted against attendance/leave by default - a floating
    /// holiday the employee can opt into.</summary>
    public bool IsOptional { get; private set; }

    /// <summary>Government-restricted holiday (India's RH concept): available only if the tenant's
    /// policy allows choosing from a restricted list, not applied to everyone automatically.</summary>
    public bool IsRestricted { get; private set; }

    public static Holiday Create(Guid holidayCalendarId, DateOnly holidayDate, string name, bool isOptional, bool isRestricted) => new()
    {
        HolidayCalendarId = Guard.NotEmpty(holidayCalendarId),
        HolidayDate = holidayDate,
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
        IsOptional = isOptional,
        IsRestricted = isRestricted,
        IsActive = true,
    };
}
