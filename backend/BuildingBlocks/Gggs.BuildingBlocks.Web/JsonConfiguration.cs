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

        // Absent rather than null: a response full of nulls is mostly noise, and the fields that
        // matter are harder to see in it.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}
