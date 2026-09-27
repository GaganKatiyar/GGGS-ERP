using Gggs.Gateway.Bff.Session;

namespace Gggs.Gateway.Bff.Infrastructure;

/// <summary>Turns the session cookie into the session, for everything downstream of it.</summary>
public sealed class SessionResolutionMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The cookie name.
    /// <para>
    /// The <c>__Host-</c> prefix is not decoration: a browser will only accept it over HTTPS, from
    /// the exact host, with no <c>Domain</c> attribute. That last part is what stops a compromised
    /// sibling subdomain from writing a cookie this gateway would then read as a session.
    /// </para>
    /// </summary>
    public const string CookieName = "__Host-gggs-session";

    public async Task InvokeAsync(HttpContext context, BffSessionStore sessions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);

        if (context.Request.Cookies.TryGetValue(CookieName, out string? sessionKey)
            && !string.IsNullOrWhiteSpace(sessionKey))
        {
            BffSession? session = await sessions.TouchAsync(
                sessionKey,
                context.Request.Headers.UserAgent.ToString(),
                context.RequestAborted);

            if (session is null)
            {
                // Expired, revoked, or replayed from elsewhere. The cookie is cleared so the browser
                // stops sending a key that will never work again.
                ClearCookie(context);
            }
            else
            {
                context.Features.Set(new BffSessionFeature(sessionKey, session));
            }
        }

        await next(context);
    }

    /// <summary>Writes the session cookie. Every attribute here is load-bearing.</summary>
    public static void SetCookie(HttpContext context, string sessionKey)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(CookieName, sessionKey, new CookieOptions
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

    public static void ClearCookie(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}

/// <summary>The resolved session, carried on the request rather than in an ambient static.</summary>
public sealed record BffSessionFeature(string SessionKey, BffSession Session);

/// <summary>Reading the session off a request without every caller knowing how it got there.</summary>
public static class BffSessionAccessor
{
    public static BffSession? GetSession(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features.Get<BffSessionFeature>()?.Session;
    }

    public static string? GetSessionKey(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features.Get<BffSessionFeature>()?.SessionKey;
    }
}
