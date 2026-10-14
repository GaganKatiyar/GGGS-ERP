using Gggs.Modules.Identity.Sessions;
using Microsoft.Extensions.Primitives;

namespace Gggs.Api.Browser;

/// <summary>
/// What the application refuses to take from a browser.
/// <para>
/// Two jobs, both about not trusting the client, which is why they sit together. The same host also
/// serves non-browser API clients with bearer tokens; a request is treated as a browser request when
/// it carries the session cookie or targets <c>/bff</c>.
/// </para>
/// <para>
/// <b>Headers are stripped.</b> Anything downstream treats as trusted context - the tenant above
/// all - is removed on the way in, so a request cannot arrive claiming to be for another customer.
/// The tenant is established downstream from the authenticated principal and nowhere else; this
/// makes sure a header cannot even be in the running.
/// </para>
/// <para>
/// <b>Unsafe browser methods need a header the browser cannot add cross-site.</b> The session cookie
/// is <c>SameSite=Strict</c>, which already stops a cross-site form post from carrying it. The custom
/// header is the second lock: adding a header to a cross-origin request requires a preflight, and
/// the application allows no cross-origin requests at all, so the preflight fails before the request is
/// ever made. Both have to be got round, not one.
/// </para>
/// </summary>
public sealed class BrowserRequestGuard(RequestDelegate next, ILogger<BrowserRequestGuard> logger)
{
    /// <summary>The header the SPA sends on every unsafe request. Its value is irrelevant; its presence is the proof.</summary>
    public const string CsrfHeaderName = "X-CSRF";

    /// <summary>Headers no caller may set, because something downstream would believe them.</summary>
    private static readonly string[] ForbiddenRequestHeaders =
    [
        "X-Tenant-Id",
        "X-Tenant-Code",
        "X-Impersonate-User",
        "X-Gggs-Internal",
    ];

    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (string header in ForbiddenRequestHeaders)
        {
            if (context.Request.Headers.Remove(header))
            {
                // Worth a log line: a legitimate client never sends these, so one that arrives is
                // either misconfigured or someone probing.
                logger.LogWarning("Stripped disallowed request header {HeaderName} from an inbound request.", header);
            }
        }

        bool isBrowserRequest = SessionCookie.IsPresent(context.Request)
            || context.Request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase);

        if (SessionCookie.IsPresent(context.Request) && context.Request.Headers.Remove("Authorization"))
        {
            // One credential per request. A browser with a session never needs a bearer token, and
            // honouring one alongside the cookie would let a script choose whose identity to use.
            logger.LogWarning("Stripped an Authorization header from a request that carries the session cookie.");
        }

        if (isBrowserRequest && RequiresCsrfHeader(context.Request)
            && !context.Request.Headers.TryGetValue(CsrfHeaderName, out StringValues _))
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: the {HeaderName} header was missing.",
                context.Request.Method,
                context.Request.Path,
                CsrfHeaderName);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9457",
                title = "Forbidden",
                status = StatusCodes.Status403Forbidden,
                success = false,
                code = "CSRF_HEADER_REQUIRED",
                message = $"Requests that change state must send the {CsrfHeaderName} header.",
                errors = Array.Empty<object>(),
                traceId = context.TraceIdentifier,
            });

            return;
        }

        await next(context);
    }

    private static bool RequiresCsrfHeader(HttpRequest request)
        => !SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
}
