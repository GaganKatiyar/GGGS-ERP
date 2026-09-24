using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Gggs.Identity.Api.Endpoints;

/// <summary>Credentials plus the hint that says which customer to look in.</summary>
public sealed record SignInRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    /// <summary>
    /// Usually taken from the subdomain the user is on. Optional: without it the email domain is
    /// used, which works for customers who have verified one.
    /// </summary>
    public string? TenantCode { get; init; }
}

/// <summary>
/// What the caller is told.
/// <para>
/// Success carries almost nothing. The session lives in an httpOnly cookie, so returning user
/// details here would put them somewhere a script on the page could read, for no benefit - the
/// client fetches them from <c>/api/v1/auth/me</c> once signed in.
/// </para>
/// </summary>
public sealed record SignInResponse
{
    public required bool Succeeded { get; init; }

    /// <summary>Stable code the UI switches on, never the message text.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The user must change their password before going any further.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>When a lockout lifts, so the UI can say so rather than "try again later".</summary>
    public DateTime? LockoutEndDate { get; init; }
}

/// <summary>
/// Local sign-in.
/// <para>
/// The flow is: work out which customer, check the credential inside that customer, then issue a
/// cookie session. Federation will add a second entry point that ends at the same place - a
/// verified user and a session - which is why the session and principal building live apart from
/// the password check.
/// </para>
/// </summary>
public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");

        // Anonymous by necessity: this is where a session comes from.
        group.MapPost("/sign-in", SignInAsync).WithName("SignIn").AllowAnonymous();
        // Cast so the return value is written to the response: a handler taking only HttpContext
        // is otherwise treated as a RequestDelegate, whose result is discarded.
        group.MapPost("/sign-out", (Delegate)SignOutAsync).WithName("SignOut").AllowAnonymous();
        group.MapGet("/me", MeAsync).WithName("CurrentUser").RequireAuthorization();

        return endpoints;
    }

    private static async Task<Results<Ok<SignInResponse>, UnauthorizedHttpResult>> SignInAsync(
        SignInRequest request,
        HttpContext httpContext,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        SignInService signInService,
        IUserRepository users,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TenantDiscoveryResult? discovered = await discovery.DiscoverAsync(
            request.TenantCode, request.Email, cancellationToken);

        if (discovered is null || !discovered.Tenant.IsOperational)
        {
            // An unknown customer and a suspended one look identical from here. Distinguishing
            // them would let anyone discover which companies are on the platform, and which are
            // behind on their bills.
            return TypedResults.Ok(Failure("INVALID_CREDENTIALS"));
        }

        // Set before any user query: users are tenant-owned, so a lookup without a tenant finds
        // nothing, and a lookup with the wrong one would find somebody else's account.
        tenantSetter.SetTenant(discovered.Tenant);

        Application.Authentication.SignInResult result =
            await signInService.SignInWithPasswordAsync(request.Email, request.Password, cancellationToken);

        if (!result.Succeeded)
        {
            return TypedResults.Ok(new SignInResponse
            {
                Succeeded = false,

                // Only the lockout is reported distinctly, because a user who cannot get in for
                // fifteen minutes needs to know that rather than retrying and extending it.
                ErrorCode = result.FailureReason == SignInFailureReason.LockedOut
                    ? "ACCOUNT_LOCKED"
                    : "INVALID_CREDENTIALS",
                LockoutEndDate = result.LockoutEndDate,
            });
        }

        User user = await users.GetAsync(result.UserId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("The user vanished between authentication and session creation.");

        ClaimsPrincipal principal = UserPrincipalFactory.Create(
            user,
            discovered.Tenant.Id,
            sessionId: Guid.CreateVersion7(),
            authenticatedAt: timeProvider.GetUtcNow(),
            mfaSatisfied: false,
            authenticationScheme: CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return TypedResults.Ok(new SignInResponse
        {
            Succeeded = true,
            MustChangePassword = result.MustChangePassword,
        });
    }

    private static async Task<NoContent> SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Who the caller is. Deliberately thin: identity and display details only, never permissions,
    /// which the client learns from what the API lets it do.
    /// </summary>
    private static Ok<CurrentUserResponse> MeAsync(ClaimsPrincipal principal)
        => TypedResults.Ok(new CurrentUserResponse
        {
            UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            TenantId = principal.FindFirstValue(GggsClaims.TenantId),
            EmployeeId = principal.FindFirstValue(GggsClaims.EmployeeId),
            UserType = principal.FindFirstValue(GggsClaims.UserType) ?? string.Empty,
        });

    private static SignInResponse Failure(string errorCode) => new()
    {
        Succeeded = false,
        ErrorCode = errorCode,
    };
}

public sealed record CurrentUserResponse
{
    public required string UserId { get; init; }

    public required string DisplayName { get; init; }

    public required string Email { get; init; }

    /// <summary>Absent for platform staff, who belong to no customer.</summary>
    public string? TenantId { get; init; }

    public string? EmployeeId { get; init; }

    public required string UserType { get; init; }
}
