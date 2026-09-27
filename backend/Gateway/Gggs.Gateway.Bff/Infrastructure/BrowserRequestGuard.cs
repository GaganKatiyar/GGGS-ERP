using Microsoft.Extensions.Primitives;

namespace Gggs.Gateway.Bff.Infrastructure;

/// <summary>
/// What the gateway refuses to take from a browser.
/// <para>
/// Two jobs, both about not trusting the client, which is why they sit together.
/// </para>
/// <para>
/// <b>Headers are stripped.</b> Anything downstream treats as trusted context - the tenant above
/// all - is removed on the way in, so a request cannot arrive claiming to be for another customer.
/// The tenant is established downstream from the authenticated principal and nowhere else; this
/// makes sure a header cannot even be in the running.
/// </para>
/// <para>
/// <b>Unsafe methods need a header the browser cannot add cross-site.</b> The session cookie is
/// <c>SameSite=Strict</c>, which already stops a cross-site form post from carrying it. The custom
/// header is the second lock: adding a header to a cross-origin request requires a preflight, and
/// the gateway allows no cross-origin requests at all, so the preflight fails before the request is
/// ever made. Both have to be got round, not one.
/// </para>
/// </summary>
public sealed class BrowserRequestGuard(RequestDelegate next, ILogger<BrowserRequestGuard> logger)
{
    /// <summary>The header the SPA sends on every unsafe request. Its value is irrelevant; its presence is the proof.</summary>
    public const string CsrfHeaderName = "X-CSRF";

    /// <summary>
    /// Headers the browser may not set, because something downstream would believe them.
    /// <para>
    /// <c>Authorization</c> is here too: the gateway supplies the credential, and a caller
    /// smuggling their own would be handing a token straight through to a service.
    /// </para>
    /// </summary>
    private static readonly string[] ForbiddenRequestHeaders =
    [
        "Authorization",
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
                // Worth a log line: a legitimate SPA never sends these, so one that arrives is
                // either a misconfigured client or someone probing.
                logger.LogWarning("Stripped disallowed request header {HeaderName} from an inbound request.", header);
            }
        }

        if (RequiresCsrfHeader(context.Request))
        {
            if (!context.Request.Headers.TryGetValue(CsrfHeaderName, out StringValues _))
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
                    errorCode = "CSRF_HEADER_REQUIRED",
                    detail = $"Requests that change state must send the {CsrfHeaderName} header.",
                });

                return;
            }
        }

        await next(context);
    }

    private static bool RequiresCsrfHeader(HttpRequest request)
        => !SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
}
