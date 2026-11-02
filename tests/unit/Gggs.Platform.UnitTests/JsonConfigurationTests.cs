using System.Text.Json;
using Gggs.BuildingBlocks.Web;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>Times on the wire: every instant says it is UTC, however it was held in memory.</summary>
public sealed class JsonConfigurationTests
{
    private static readonly JsonSerializerOptions Options = Wire();

    private static JsonSerializerOptions Wire()
    {
        var options = new JsonSerializerOptions();
        JsonConfiguration.Apply(options);
        return options;
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void AnInstant_GoesOutMarkedAsUtc(DateTimeKind kind)
        => JsonSerializer.Serialize(new DateTime(2026, 11, 1, 13, 40, 12, kind), Options).ShouldBe("\"2026-11-01T13:40:12Z\"");

    [Fact]
    public void ALocalTime_IsConverted_NotRelabelled()
    {
        DateTime local = new DateTime(2026, 11, 1, 13, 40, 12, DateTimeKind.Utc).ToLocalTime();

        JsonSerializer.Serialize(local, Options).ShouldBe("\"2026-11-01T13:40:12Z\"");
    }

    [Fact]
    public void FractionsOfASecond_AreKept_AndAMissingOneStaysAbsent()
    {
        JsonSerializer.Serialize(new DateTime(2026, 11, 1, 13, 40, 12, 784, DateTimeKind.Unspecified), Options).ShouldBe("\"2026-11-01T13:40:12.784Z\"");
        JsonSerializer.Serialize(new Holder(null), Options).ShouldBe("{}");
        JsonSerializer.Serialize(new Holder(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified)), Options).ShouldBe("{\"at\":\"2026-11-01T00:00:00Z\"}");
    }

    [Fact]
    public void WhatComesIn_IsReadAsBefore()
    {
        JsonSerializer.Deserialize<DateTime>("\"2026-11-01T13:40:12Z\"", Options).Kind.ShouldBe(DateTimeKind.Utc);
        JsonSerializer.Deserialize<DateTime>("\"2026-11-01T13:40:12\"", Options).ShouldBe(new DateTime(2026, 11, 1, 13, 40, 12, DateTimeKind.Unspecified));
    }

    [Fact]
    public void PlainDates_AreLeftAlone()
        => JsonSerializer.Serialize(new DateOnly(2026, 11, 1), Options).ShouldBe("\"2026-11-01\"");

    private sealed record Holder(DateTime? At);
}
