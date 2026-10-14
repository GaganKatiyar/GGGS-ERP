using System.Security.Claims;

namespace Gggs.Modules.Identity.Sessions;

/// <summary>One claim as the session store keeps it.</summary>
public sealed record SessionClaim(string Type, string Value, string ValueType);

/// <summary>
/// What the server remembers about a signed-in browser.
/// <para>
/// The browser gets a session key in a cookie and nothing else. Everything here stays on the server,
/// which is the point of the pattern: a script that reaches the page - through a dependency, an
/// extension, or a cross-site injection - finds no credential to take, because there is none there.
/// </para>
/// <para>
/// In the monolith (ADR-12) the session holds the signed-in principal's claims rather than an
/// access token: nothing is forwarded to another process, so there is no token to keep. Claims are
/// rebuilt from the account record every <see cref="BffSessionOptions.RevalidateInterval"/>, which
/// is what the ten-minute token refresh used to guarantee - a disabled account or suspended tenant
/// stops working within that window.
/// </para>
/// </summary>
public sealed record BffSession
{
    public required Guid UserId { get; init; }

    /// <summary>Absent for platform staff, who belong to no customer.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>The principal's claims, exactly as <c>UserPrincipalFactory</c> built them.</summary>
    public required IReadOnlyList<SessionClaim> Claims { get; init; }

    public required DateTimeOffset SignedInAt { get; init; }

    /// <summary>Last request on this session, which is what the idle timeout measures.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>When the claims were last rebuilt from the account record.</summary>
    public required DateTimeOffset LastValidatedAt { get; init; }

    /// <summary>
    /// When the session ends regardless of activity. An idle timeout alone lets a session live
    /// forever on a machine nobody has walked away from, which is not the same as being trusted.
    /// </summary>
    public required DateTimeOffset AbsoluteExpiresAt { get; init; }

    /// <summary>
    /// Bound to the browser that signed in. A stolen session key replayed from elsewhere usually
    /// carries a different user agent, and the mismatch is worth refusing and recording.
    /// </summary>
    public string? UserAgentHash { get; init; }

    public bool NeedsRevalidation(DateTimeOffset now, TimeSpan interval) => now >= LastValidatedAt.Add(interval);

    public ClaimsPrincipal ToPrincipal(string authenticationScheme)
        => new(new ClaimsIdentity(
            Claims.Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType)),
            authenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));

    public static IReadOnlyList<SessionClaim> ClaimsOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return [.. principal.Claims.Select(claim => new SessionClaim(claim.Type, claim.Value, claim.ValueType))];
    }
}

/// <summary>Browser session settings.</summary>
public sealed class BffSessionOptions
{
    public const string SectionName = "Gggs:Bff:Session";

    /// <summary>
    /// How long a session survives without a request. Thirty minutes matches the architecture's
    /// default and is what an HR user leaving their desk expects.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Hard ceiling, activity or not.</summary>
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Whether a change of browser ends the session. On by default; off only for the rare
    /// deployment behind a proxy that rewrites user agents.
    /// </summary>
    public bool BindToUserAgent { get; set; } = true;

    /// <summary>
    /// How often the claims are rebuilt from the account record. Bounds how long a disabled account,
    /// a removed role's display claims, or a suspended tenant keep working in an open browser.
    /// Permissions themselves are resolved per request and are never stale.
    /// </summary>
    public TimeSpan RevalidateInterval { get; set; } = TimeSpan.FromMinutes(5);
}
