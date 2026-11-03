using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Web;

/// <summary>The named limits endpoints opt into with <c>RequireRateLimiting</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Anything that checks a credential - passwords, step-up re-entry, federated returns, token
    /// requests - per client address. Lockout protects one account; this protects all of them from
    /// one address trying a common password against each.
    /// </summary>
    public const string SignIn = "sign-in";

    /// <summary>Assistant turns, per signed-in user: each one costs model tokens.</summary>
    public const string Assistant = "assistant";

    /// <summary>Webhooks from outside systems, per client address: generous, since one busy push can send many.</summary>
    public const string Webhook = "webhook";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "Gggs:RateLimiting";

    public int SignInPerMinute { get; set; } = 20;

    public int AssistantPerMinute { get; set; } = 12;

    public int WebhookPerMinute { get; set; } = 300;

    /// <summary>Calls one API key may make a minute, so a runaway integration cannot crowd out people.</summary>
    public int ApiKeyPerMinute { get; set; } = 600;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddGggsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>().Bind(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { code = "RATE_LIMITED", title = "Too many requests", status = 429, detail = "Too many attempts in a short time. Wait a minute and try again." },
                    cancellationToken);
            };

            // API-key callers share a per-key window across every endpoint; people are unaffected.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Headers.TryGetValue("X-Api-Key", out var header) && header.ToString() is { Length: > 0 } key
                    ? RateLimitPartition.GetFixedWindowLimiter($"key:{KeyPartition(key)}", _ => Window(Limits(http).ApiKeyPerMinute))
                    : RateLimitPartition.GetNoLimiter("people"));

            options.AddPolicy(RateLimitPolicies.SignIn, http => RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{http.Connection.RemoteIpAddress}",
                _ => Window(Limits(http).SignInPerMinute)));

            options.AddPolicy(RateLimitPolicies.Webhook, http => RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{http.Connection.RemoteIpAddress}",
                _ => Window(Limits(http).WebhookPerMinute)));

            options.AddPolicy(RateLimitPolicies.Assistant, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue("sub") is { } user ? $"user:{user}" : $"ip:{http.Connection.RemoteIpAddress}",
                _ => Window(Limits(http).AssistantPerMinute)));
        });

        return services;
    }

    /// <summary>The key without its secret part, so the secret is never held as a partition name.</summary>
    private static string KeyPartition(string key)
    {
        int at = key.LastIndexOf('_');
        return at > 0 ? key[..at] : key;
    }

    private static RateLimitOptions Limits(HttpContext http) => http.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitOptions>>().CurrentValue;

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = Math.Max(1, permits),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
