using Microsoft.Extensions.Options;

namespace Gggs.Api.Browser;

/// <summary>
/// The response headers that constrain what the browser will do with our page.
/// <para>
/// Set on every response, including the SPA's own document and static files, which this host
/// serves from the same origin as the API.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeaderOptions> options)
{
    private readonly SecurityHeaderOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHeaderDictionary headers = context.Response.Headers;

        // A Content-Security-Policy that names no host but our own. The SPA is built with hashed
        // asset names and no inline scripts, so 'self' is enough and there is no need for the
        // 'unsafe-inline' that makes most policies decorative.
        //
        // Two entries carry more weight than the rest. object-src 'none' removes plugin embedding,
        // historically the easiest way around a script restriction; frame-ancestors 'none' means no
        // other site can put this application in an iframe, which is what clickjacking needs.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self'; " +

            // Angular writes component styles into the document at runtime, so inline styles cannot
            // be forbidden without breaking every component. Style injection is a defacement risk
            // rather than a code-execution one, which is why this is the one concession.
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self'; " +

            // Only our own origin: the SPA talks to the gateway and nothing else, so an injected
            // script has nowhere to send what it collects.
            $"connect-src 'self'{_options.AdditionalConnectSources}; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "upgrade-insecure-requests";

        // Stops a browser from guessing a type we did not declare - the trick that turns an uploaded
        // text file into executable script.
        headers["X-Content-Type-Options"] = "nosniff";

        // A referrer is not sent off-site, so a URL containing an employee id never reaches a third
        // party in a Referer header.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Nothing in an HR application needs a camera, a microphone or a payment handler. Geolocation
        // is left to the attendance feature to request explicitly if geo-fencing is ever enabled.
        headers["Permissions-Policy"] = "camera=(), microphone=(), payment=(), usb=(), geolocation=()";

        // Belt and braces alongside frame-ancestors, for the handful of clients that still only
        // understand this one.
        headers["X-Frame-Options"] = "DENY";

        if (_options.EnableHsts)
        {
            // A year, with subdomains, and preload-eligible. Deliberately off by default so a
            // developer on localhost does not pin their browser to HTTPS for a year by accident.
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        await next(context);
    }
}

/// <summary>The few parts of the policy a deployment legitimately needs to change.</summary>
public sealed class SecurityHeaderOptions
{
    public const string SectionName = "Gggs:SecurityHeaders";

    /// <summary>
    /// On in production, off for local HTTP development. Turning it on is close to irreversible for
    /// a given host, which is why it is opt-in rather than opt-out.
    /// </summary>
    public bool EnableHsts { get; set; }

    /// <summary>
    /// Extra origins the page may call, appended to <c>connect-src</c>, for example a telemetry
    /// endpoint. Each one is a place an injected script could send data, so the list stays short.
    /// </summary>
    public string AdditionalConnectSources { get; set; } = string.Empty;
}
