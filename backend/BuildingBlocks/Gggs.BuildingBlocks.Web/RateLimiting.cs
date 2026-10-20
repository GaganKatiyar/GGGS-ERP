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
    /// Anything that checks a credential - passwords, authenticator codes, federated returns, token
    /// requests - per client address. Lockout protects one account; this protects all of them from
    /// one address trying a common password against each.
    /// </summary>
    public const string SignIn = "sign-in";

    /// <summary>Assistant turns, per signed-in user: each one costs model tokens.</summary>
    public const string Assistant = "assistant";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "Gggs:RateLimiting";

    public int SignInPerMinute { get; set; } = 20;

    public int AssistantPerMinute { get; set; } = 12;
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

            options.AddPolicy(RateLimitPolicies.SignIn, http => RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{http.Connection.RemoteIpAddress}",
                _ => Window(Limits(http).SignInPerMinute)));

            options.AddPolicy(RateLimitPolicies.Assistant, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirstValue("sub") is { } user ? $"user:{user}" : $"ip:{http.Connection.RemoteIpAddress}",
                _ => Window(Limits(http).AssistantPerMinute)));
        });

        return services;
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
