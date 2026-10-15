namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// The current tenant's local time. Attendance days, leave dates and "today" are the tenant's,
/// not the server's: a punch at 02:00 UTC is the same working day in Kolkata as one at 20:00 UTC
/// the evening before, and which day it is decides who was absent.
/// </summary>
public interface ITenantClock
{
    Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Today's date where the tenant is.</summary>
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);

    /// <summary>A UTC instant as the tenant's local wall-clock time.</summary>
    Task<DateTime> ToLocalAsync(DateTime utc, CancellationToken cancellationToken = default);

    /// <summary>The UTC instant a tenant-local date and time stands for.</summary>
    Task<DateTime> ToUtcAsync(DateOnly localDate, TimeOnly localTime, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the zone from the resolved tenant on a request, and from the registry in background work,
/// where only the tenant id travels with the job. An unknown zone id falls back to UTC rather than
/// failing the request.
/// </summary>
public sealed class TenantClock(ITenantContext tenantContext, ITenantRegistry registry, TimeProvider timeProvider) : ITenantClock
{
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        if (_zone is not null)
        {
            return _zone;
        }

        string? zoneId = tenantContext.Tenant?.DefaultTimeZone
            ?? (await registry.GetAsync(tenantContext.TenantId, cancellationToken))?.DefaultTimeZone;

        _zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out TimeZoneInfo? found) ? found : TimeZoneInfo.Utc;
        return _zone;
    }

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => DateOnly.FromDateTime(await ToLocalAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken));

    public async Task<DateTime> ToLocalAsync(DateTime utc, CancellationToken cancellationToken = default)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), await GetTimeZoneAsync(cancellationToken));

    public async Task<DateTime> ToUtcAsync(DateOnly localDate, TimeOnly localTime, CancellationToken cancellationToken = default)
    {
        TimeZoneInfo zone = await GetTimeZoneAsync(cancellationToken);
        DateTime local = DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified);

        // A wall-clock time skipped by a daylight-saving jump does not exist; move past the gap.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
