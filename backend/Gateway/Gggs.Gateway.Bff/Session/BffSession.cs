namespace Gggs.Gateway.Bff.Session;

/// <summary>
/// What the gateway remembers about a signed-in browser.
/// <para>
/// The browser gets a session key in a cookie and nothing else. Everything here stays on the server,
/// which is the entire point of the pattern: a script that reaches the page - through a dependency,
/// an extension, or a cross-site injection - finds no credential to take, because there is none
/// there to find.
/// </para>
/// <para>
/// <see cref="UpstreamCredential"/> is Identity's own session cookie today and will be an access
/// token once the OIDC server ships. Both are bearer credentials from the browser's point of view,
/// and both are handled the same way: stored here, attached by the proxy, never serialized to the
/// client.
/// </para>
/// </summary>
public sealed record BffSession
{
    public required Guid UserId { get; init; }

    public required string UpstreamCredential { get; init; }

    /// <summary>Absent for platform staff, who belong to no customer.</summary>
    public Guid? TenantId { get; init; }

    public required DateTimeOffset SignedInAt { get; init; }

    /// <summary>Last request on this session, which is what the idle timeout measures.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }

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
}

/// <summary>Gateway session settings.</summary>
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
}
