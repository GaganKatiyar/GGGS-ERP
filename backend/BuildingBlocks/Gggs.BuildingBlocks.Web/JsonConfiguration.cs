using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// The JSON conventions every service's API uses, so one client can talk to all of them without
/// per-service special cases.
/// </summary>
public static class JsonConfiguration
{
    /// <summary>
    /// Applies the platform's wire format.
    /// <para>
    /// Enums travel as strings. A numeric enum is unreadable in a log, silently changes meaning if
    /// anyone reorders the declaration, and forces every client to keep its own copy of the
    /// mapping. <c>"Branch"</c> means the same thing in five years; <c>5</c> does not.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsJson(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JsonOptions>(options => Apply(options.SerializerOptions));

        return services;
    }

    /// <summary>Applied to a serializer directly, for callers outside the HTTP pipeline.</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeConverter());

        // Absent rather than null: a response full of nulls is mostly noise, and the fields that
        // matter are harder to see in it.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}

/// <summary>
/// Every instant the platform holds is UTC, but one read back from SQL Server has lost that fact:
/// it arrives with an unspecified kind and would go out with no zone. A client then reads it as its
/// own local time - five and a half hours out in India. So each goes out marked as the UTC it is;
/// reading is left as System.Text.Json does it.
/// </summary>
internal sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
