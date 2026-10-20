using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Says why a request was forbidden when the answer is "sign in again", not "you may not": a held
/// permission whose step-up window has passed. The browser then asks for a password or authenticator
/// code and retries, rather than showing an access-denied page to someone who has access.
/// </summary>
public sealed class StepUpResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string ErrorCode = "STEP_UP_REQUIRED";

    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Message == AuthorizationFailureReasons.StepUpRequired) == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_user_authentication\"";
            await context.Response.WriteAsJsonAsync(new
            {
                code = ErrorCode,
                title = "Confirm it's you",
                status = 403,
                detail = "This action needs you to confirm your identity again.",
            });
            return;
        }

        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
