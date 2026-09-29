namespace Gggs.Time.Api.Contracts;

public sealed record HolidayCalendarResponse(Guid Id, string Name, int Year, string CountryCode, string? StateCode);

public sealed record CreateHolidayCalendarBody(string Name, int Year, string CountryCode, string? StateCode);

public sealed record HolidayResponse(Guid Id, Guid HolidayCalendarId, DateOnly HolidayDate, string Name, bool IsOptional, bool IsRestricted);

public sealed record AddHolidayBody(DateOnly HolidayDate, string Name, bool IsOptional, bool IsRestricted);
