using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Identity.Sessions;

/// <summary>The session cookie, and the rules for writing it.</summary>
public static class SessionCookie
{
    /// <summary>
    /// The cookie name.
    /// <para>
    /// The <c>__Host-</c> prefix is not decoration: a browser will only accept it over HTTPS, from
    /// the exact host, with no <c>Domain</c> attribute. That last part is what stops a compromised
    /// sibling subdomain from writing a cookie this application would then read as a session.
    /// </para>
    /// </summary>
    public const string Name = "__Host-gggs-session";

    /// <summary>Writes the session cookie. Every attribute here is load-bearing.</summary>
    public static void Set(HttpContext context, string sessionKey)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(Name, sessionKey, new CookieOptions
        {
            // Nothing on the page can read it, so an injected script has no credential to send on.
            HttpOnly = true,

            // Required by the __Host- prefix, and correct regardless.
            Secure = true,

            // No cross-site request carries it, which is the first of the two CSRF defences.
            SameSite = SameSiteMode.Strict,

            // Also required by the prefix: path must be root and no domain may be set.
            Path = "/",

            // A session cookie with no expiry: the server decides when the session ends, and a
            // persistent cookie would outlive the record it points at.
            IsEssential = true,
        });
    }

    public static void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    public static bool IsPresent(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.ContainsKey(Name);
    }
}

/// <summary>The resolved session, carried on the request so sign-out can find and revoke it.</summary>
public sealed record BffSessionFeature(string SessionKey, BffSession Session);

/// <summary>
/// Authenticates a browser request from its session cookie.
/// <para>
/// The in-process replacement for the old gateway's session middleware plus bearer-token proxy:
/// the session record's claims become <see cref="HttpContext.User"/> directly, and everything after
/// authentication - tenant resolution, permissions, auditing - sees the same principal a bearer
/// token would have produced.
/// </para>
/// <para>
/// Every <see cref="BffSessionOptions.RevalidateInterval"/> the claims are rebuilt from the account
/// record through <see cref="PrincipalRevalidator"/>, under a per-session lock so a dashboard firing
/// ten requests at once revalidates once rather than ten times. An account that can no longer sign
/// in, or a tenant that is no longer operational, ends the session there and then.
/// </para>
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    BffSessionStore sessions,
    IOptions<BffSessionOptions> sessionOptions,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "Session";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RevalidationLocks = new(StringComparer.Ordinal);

    private readonly BffSessionOptions _sessionOptions = sessionOptions.Value;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionCookie.Name, out string? sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
        {
            return AuthenticateResult.NoResult();
        }

        BffSession? session = await sessions.TouchAsync(sessionKey, Request.Headers.UserAgent.ToString(), Context.RequestAborted);

        if (session is not null && session.NeedsRevalidation(timeProvider.GetUtcNow(), _sessionOptions.RevalidateInterval))
        {
            session = await RevalidateAsync(sessionKey, Context.RequestAborted);
        }

        if (session is null)
        {
            // Expired, revoked, replayed from elsewhere, or the account no longer qualifies. The
            // cookie is cleared so the browser stops sending a key that will never work again.
            SessionCookie.Clear(Context);
            return AuthenticateResult.NoResult();
        }

        Context.Features.Set(new BffSessionFeature(sessionKey, session));

        ClaimsPrincipal principal = session.ToPrincipal(Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// An API answers with a status code, never a redirect to a login page: the SPA decides what to
    /// show on a 401.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private async Task<BffSession?> RevalidateAsync(string sessionKey, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = RevalidationLocks.GetOrAdd(sessionKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);

        try
        {
            // Re-read under the lock: whoever held it first has probably already revalidated.
            BffSession? current = await sessions.PeekAsync(sessionKey, cancellationToken);

            if (current is null || !current.NeedsRevalidation(timeProvider.GetUtcNow(), _sessionOptions.RevalidateInterval))
            {
                return current;
            }

            PrincipalRevalidator revalidator = Context.RequestServices.GetRequiredService<PrincipalRevalidator>();
            ClaimsPrincipal? rebuilt = await revalidator.RebuildAsync(current.ToPrincipal(Scheme.Name), Scheme.Name, cancellationToken);

            if (rebuilt is null)
            {
                Logger.LogInformation("Session for user {UserId} could not be revalidated; it has been ended.", current.UserId);
                await sessions.RemoveAsync(sessionKey, cancellationToken);
                return null;
            }

            return await sessions.UpdateClaimsAsync(sessionKey, current, BffSession.ClaimsOf(rebuilt), cancellationToken);
        }
        finally
        {
            gate.Release();

            // Dropped once nobody is waiting, so a long-running process does not accumulate one
            // semaphore per session it has ever seen.
            if (gate.CurrentCount == 1)
            {
                RevalidationLocks.TryRemove(sessionKey, out _);
            }
        }
    }
}
