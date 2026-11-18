using Gggs.BuildingBlocks.MultiTenancy;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

/// <summary>Times in emails and messages: the organisation's own time, 12-hour, with its zone.</summary>
public sealed class TenantClockFormatTests
{
    private static TimeZoneInfo India => TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    [Fact]
    public void An_instant_reads_as_the_organisations_time_with_AM_or_PM()
    {
        // 03:16 UTC is 8:46 in the morning in India.
        TenantClock.Format(new DateTime(2026, 11, 20, 3, 16, 0, DateTimeKind.Utc), India).ShouldBe("20 Nov 2026, 8:46 AM IST");
        TenantClock.Format(new DateTime(2026, 11, 20, 13, 0, 0, DateTimeKind.Utc), India, withDate: false).ShouldBe("6:30 PM IST");
    }

    [Fact]
    public void Midnight_and_noon_are_twelve_not_zero()
    {
        TenantClock.Format(new DateTime(2026, 11, 19, 18, 30, 0, DateTimeKind.Utc), India).ShouldBe("20 Nov 2026, 12:00 AM IST");
        TenantClock.Format(new DateTime(2026, 11, 20, 6, 30, 0, DateTimeKind.Utc), India, withDate: false).ShouldBe("12:00 PM IST");
    }

    [Fact]
    public void Another_zone_shows_its_offset_and_UTC_says_so()
    {
        TenantClock.Format(new DateTime(2026, 11, 20, 3, 16, 0, DateTimeKind.Utc), TimeZoneInfo.Utc).ShouldBe("20 Nov 2026, 3:16 AM UTC");

        TimeZoneInfo dubai = TimeZoneInfo.CreateCustomTimeZone("Test/Dubai", TimeSpan.FromHours(4), "Dubai", "Dubai");
        TenantClock.Format(new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), dubai, withDate: false).ShouldBe("2:00 PM UTC+04:00");
    }
}
