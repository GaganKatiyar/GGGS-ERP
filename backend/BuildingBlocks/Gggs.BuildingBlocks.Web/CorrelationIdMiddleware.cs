using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Gives every request one id that follows it through services, events, jobs and audit rows.
/// <para>
/// Support questions are almost always "what happened to this one action?", and the answer has to
/// span an API call, a queued message and a background job. The W3C trace id is used as the
/// correlation id so OpenTelemetry traces and our own audit records line up without a second
/// identifier to reconcile.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;

        // Echo it back so a caller (and the browser network tab) can quote it in a bug report.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues supplied)
            && !string.IsNullOrWhiteSpace(supplied))
        {
            string candidate = supplied.ToString();

            // Accept a caller-supplied id, but only a sane one: it ends up in logs and dashboards.
            if (candidate.Length <= 128 && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
