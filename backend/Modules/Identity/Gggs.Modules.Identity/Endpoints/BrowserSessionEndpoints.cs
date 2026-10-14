using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>Credentials as the sign-in page sends them.</summary>
public sealed record BffLoginRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    /// <summary>
    /// Which customer to look in. Usually filled from the subdomain; optional for customers who have
    /// verified an email domain, and absent for platform staff.
    /// </summary>
    public string? TenantCode { get; init; }
}

/// <summary>
/// The answer to a sign-in attempt.
/// <para>
/// Carries no session details. The cookie is set by the response, and the client then asks
/// <c>/bff/user</c> - so there is one place that describes a session rather than two that can
/// disagree.
/// </para>
/// </summary>
public sealed record BffLoginResponse
{
    public required bool Succeeded { get; init; }

    /// <summary>Stable code the UI switches on, never message text. See <see cref="SignInErrorCodes"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The user must change their password before going any further.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>When a lockout lifts, so the UI can say so rather than "try again later".</summary>
    public DateTime? LockoutEndDate { get; init; }
}

/// <summary>
/// The browser's session endpoints: where a credential is exchanged for a cookie and never crosses
/// back. The browser holds an opaque session key and nothing else.
/// </summary>
public static class BrowserSessionEndpoints
{
    public static IEndpointRouteBuilder MapBrowserSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/bff").WithTags("Session");

        // Anonymous by necessity: this is where a session comes from, and where it ends.
        group.MapPost("/login", LoginAsync).WithName("BffLogin").AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).WithName("BffLogout").AllowAnonymous();

        // Anonymous so an anonymous visitor gets a plain 401 to act on rather than a challenge: the
        // SPA calls this on every load precisely to find out whether anyone is signed in.
        group.MapGet("/user", GetUserAsync).WithName("BffUser").AllowAnonymous();

        return endpoints;
    }

    private static async Task<Ok<BffLoginResponse>> LoginAsync(
        BffLoginRequest body,
        HttpContext context,
        PasswordSignInService passwordSignIn,
        BffSessionStore sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(context);

        // An existing session is discarded first. Signing in while already signed in otherwise
        // leaves the previous session alive and revocable by nobody.
        if (context.Features.Get<BffSessionFeature>() is { } existing)
        {
            await sessions.RemoveAsync(existing.SessionKey, cancellationToken);
        }

        PasswordSignInOutcome outcome = await passwordSignIn.SignInAsync(
            body.Email,
            body.Password,
            body.TenantCode,
            SessionAuthenticationHandler.SchemeName,
            cancellationToken);

        if (!outcome.Succeeded)
        {
            return TypedResults.Ok(new BffLoginResponse
            {
                Succeeded = false,
                ErrorCode = outcome.ErrorCode ?? SignInErrorCodes.InvalidCredentials,
                LockoutEndDate = outcome.LockoutEndDate,
            });
        }

        ClaimsPrincipal principal = outcome.Principal!;

        string sessionKey = await sessions.CreateAsync(
            outcome.SubjectId!.Value,
            Guid.TryParse(principal.FindFirstValue(GggsClaims.TenantId), out Guid tenantId) ? tenantId : null,
            BffSession.ClaimsOf(principal),
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        SessionCookie.Set(context, sessionKey);

        return TypedResults.Ok(new BffLoginResponse
        {
            Succeeded = true,
            MustChangePassword = outcome.MustChangePassword,
        });
    }

    private static async Task<NoContent> LogoutAsync(
        HttpContext context,
        BffSessionStore sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Features.Get<BffSessionFeature>() is { } session)
        {
            await sessions.RemoveAsync(session.SessionKey, cancellationToken);
        }

        // Cleared even when there was no session: a browser holding a key we do not recognise should
        // stop sending it.
        SessionCookie.Clear(context);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The signed-in user, or 401.
    /// <para>
    /// Built per call rather than cached in the session record, so a role change or a suspended
    /// tenant is reflected on the next page load instead of whenever the session happens to end.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult>> GetUserAsync(
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        ITenantRegistry registry,
        IPermissionProvider permissions,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await MeEndpoints.BuildSessionAsync(principal, tenantContext, registry, permissions, cancellationToken));
    }
}
