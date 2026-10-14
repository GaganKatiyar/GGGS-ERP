using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Web;

/// <summary>Problem details that always carry the PeopleOS error envelope.</summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Registers problem details so responses the framework produces on its own - a 401 from the
    /// authentication middleware, a 404 for an unknown route, a 405 - have the same shape as those
    /// from <see cref="GlobalExceptionHandler"/>. A client should never need two error parsers.
    /// </summary>
    public static IServiceCollection AddGggsProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            IDictionary<string, object?> extensions = context.ProblemDetails.Extensions;

            extensions.TryAdd("success", false);
            extensions.TryAdd("code", CodeFor(context.ProblemDetails.Status));
            extensions.TryAdd("message", context.ProblemDetails.Detail ?? context.ProblemDetails.Title);
            extensions.TryAdd("errors", Array.Empty<FieldError>());
            extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });

        return services;
    }

    private static string CodeFor(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => "BAD_REQUEST",
        StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status412PreconditionFailed => "CONCURRENCY_CONFLICT",
        StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
        StatusCodes.Status422UnprocessableEntity => "BUSINESS_RULE_VIOLATED",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        _ => "INTERNAL_ERROR",
    };
}
