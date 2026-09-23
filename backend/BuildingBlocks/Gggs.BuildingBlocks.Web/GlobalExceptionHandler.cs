using FluentValidation;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Turns every exception into an RFC 9457 problem document with a stable error code and the trace
/// id, and keeps internal detail out of the response.
/// <para>
/// Two mappings are deliberate rather than obvious. A resource in another tenant returns 404, not
/// 403, because 403 would confirm that the record exists. And a tenant mismatch is logged as a
/// security incident even though the caller sees the same bland 404.
/// </para>
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller went away; there is nobody to answer.
            return true;
        }

        ProblemDetails problem = Map(httpContext, exception);
        LogException(httpContext, exception, problem.Status ?? StatusCodes.Status500InternalServerError);

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        problem.Instance = httpContext.Request.Path;

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails Map(HttpContext context, Exception exception) => exception switch
    {
        ValidationException validation => ValidationProblem(validation),

        DomainRuleException domainRule => Problem(
            StatusCodes.Status422UnprocessableEntity,
            "Business rule violated",
            domainRule.Message,
            domainRule.ErrorCode,
            domainRule.Context),

        NotFoundException notFound => Problem(
            StatusCodes.Status404NotFound,
            "Not found",
            notFound.Message,
            notFound.ErrorCode),

        // Another tenant's data was reached. The client learns nothing; we alert internally.
        TenantMismatchException => Problem(
            StatusCodes.Status404NotFound,
            "Not found",
            "The requested resource was not found.",
            "NOT_FOUND"),

        ForbiddenException forbidden => Problem(
            StatusCodes.Status403Forbidden,
            "Forbidden",
            forbidden.Message,
            forbidden.ErrorCode),

        TenantUnavailableException unavailable => Problem(
            StatusCodes.Status403Forbidden,
            "Tenant unavailable",
            unavailable.Message,
            unavailable.ErrorCode),

        FeatureNotEnabledException feature => Problem(
            StatusCodes.Status403Forbidden,
            "Feature not enabled",
            feature.Message,
            feature.ErrorCode),

        TenantNotResolvedException notResolved => Problem(
            StatusCodes.Status401Unauthorized,
            "Unauthorized",
            "The request could not be associated with a tenant.",
            notResolved.ErrorCode),

        UnauthorizedAccessException => Problem(
            StatusCodes.Status401Unauthorized,
            "Unauthorized",
            "Authentication is required.",
            "UNAUTHORIZED"),

        DbUpdateConcurrencyException => Problem(
            StatusCodes.Status412PreconditionFailed,
            "Concurrent modification",
            "The record was changed by someone else. Reload it and try again.",
            "CONCURRENCY_CONFLICT"),

        _ => Problem(
            StatusCodes.Status500InternalServerError,
            "Unexpected error",
            // Never echo exception text: it leaks schema, paths and sometimes data.
            $"An unexpected error occurred. Quote trace id {context.TraceIdentifier} when reporting it.",
            "INTERNAL_ERROR"),
    };

    private static ProblemDetails ValidationProblem(ValidationException validation)
    {
        Dictionary<string, string[]> errors = validation.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

        var problem = Problem(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            "One or more fields are invalid.",
            "VALIDATION_FAILED");

        problem.Extensions["errors"] = errors;
        return problem;
    }

    private static ProblemDetails Problem(
        int status,
        string title,
        string detail,
        string errorCode,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://docs.gggshr.com/errors/{errorCode.ToLowerInvariant()}",
        };

        problem.Extensions["errorCode"] = errorCode;

        if (extensions is { Count: > 0 })
        {
            problem.Extensions["context"] = extensions;
        }

        return problem;
    }

    private void LogException(HttpContext context, Exception exception, int status)
    {
        switch (exception)
        {
            case TenantMismatchException mismatch:
                // The one case that is always investigated: it means a request nearly crossed a
                // tenant boundary, so it is logged loudly even though the response is a plain 404.
                logger.LogCritical(
                    exception,
                    "Tenant isolation violation on {Path}: expected tenant {ExpectedTenantId}, entity {EntityType} belonged to {ActualTenantId}.",
                    context.Request.Path,
                    mismatch.ExpectedTenantId,
                    mismatch.EntityType,
                    mismatch.ActualTenantId);
                break;

            case ValidationException or DomainRuleException or NotFoundException:
                logger.LogInformation(
                    "Request to {Path} rejected with {StatusCode}: {Message}",
                    context.Request.Path,
                    status,
                    exception.Message);
                break;

            case ForbiddenException or TenantUnavailableException or FeatureNotEnabledException:
                logger.LogWarning(
                    "Request to {Path} denied with {StatusCode}: {Message}",
                    context.Request.Path,
                    status,
                    exception.Message);
                break;

            default:
                logger.LogError(exception, "Unhandled exception on {Path}.", context.Request.Path);
                break;
        }
    }
}
