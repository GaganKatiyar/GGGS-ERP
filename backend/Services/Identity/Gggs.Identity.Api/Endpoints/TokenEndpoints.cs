using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Domain.PlatformStaff;
using Gggs.Identity.Domain.Users;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gggs.Identity.Api.Endpoints;

/// <summary>
/// The token endpoint.
/// <para>
/// This is the only place an access token is minted, and every service on the platform trusts what
/// it says. Two claims matter more than the rest: <c>tid</c>, which decides whose data the caller can
/// reach, and <c>pv</c>, which decides when a cached permission set is stale. Both are taken from the
/// <b>user record</b> - never from the request - so asking for another tenant is not a thing a caller
/// can do.
/// </para>
/// <para>
/// Two grants are supported. <c>password</c> is for the first-party gateway, which has just collected
/// the credential itself; <c>refresh_token</c> keeps a browser session alive without holding the
/// password. Nothing else is enabled, so there is no authorization-code flow to misconfigure until
/// federation actually needs one.
/// </para>
/// <para>
/// The password grant serves two disjoint audiences with one endpoint: tenant users and our own
/// platform staff. Which one a request resolves to is decided here, never asked for - see
/// <see cref="ExchangePasswordAsync"/>.
/// </para>
/// </summary>
public static class TokenEndpoints
{
    /// <summary>
    /// The tenant hint, passed alongside the credentials.
    /// <para>
    /// A hint about <em>where to look</em>, not an assertion about who the user is: it selects which
    /// customer's directory is searched, and the tenant the token is issued for still comes from the
    /// user record that search finds. Its absence is also a signal: a request with no tenant hint at
    /// all is what a platform staff sign-in looks like, since platform staff have no tenant to hint.
    /// </para>
    /// </summary>
    public const string TenantCodeParameter = "tenant_code";

    /// <summary>The scope every first-party client asks for. One resource, because there is one product.</summary>
    public const string ApiScope = "gggs.api";

    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Anonymous by necessity: this is where credentials are exchanged for a token, so there is
        // nothing to authenticate with yet. OpenIddict validates the client and the grant itself.
        endpoints.MapPost("/connect/token", ExchangeAsync)
            .WithName("Token")
            .WithTags("Authentication")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext httpContext,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        SignInService signInService,
        IUserRepository users,
        IPlatformStaffRepository platformStaff,
        PlatformStaffSignInService platformStaffSignIn,
        ITenantRegistry registry,
        TimeProvider timeProvider,
        ILogger<SignInService> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request could not be read from the context.");

        if (request.IsPasswordGrantType())
        {
            return await ExchangePasswordAsync(
                request,
                discovery,
                tenantSetter,
                signInService,
                users,
                platformStaff,
                platformStaffSignIn,
                timeProvider,
                cancellationToken);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await ExchangeRefreshTokenAsync(
                httpContext, tenantSetter, users, platformStaff, registry, timeProvider, logger, cancellationToken);
        }

        return Forbid(Errors.UnsupportedGrantType, "That grant type is not supported.");
    }

    /// <summary>
    /// Resolves a password grant to either a tenant user or a platform staff member.
    /// <para>
    /// The rule is the presence of <see cref="TenantCodeParameter"/>, not the email address. A
    /// request naming a tenant is unambiguously about that tenant's own directory, so it goes
    /// straight to tenant discovery exactly as before - the common case is untouched. A request
    /// naming none is what the platform console sends, and only then is the platform staff table
    /// consulted. The two tables are never both queried for the same request, so there is no
    /// question of which one wins if an address happened to exist in both.
    /// </para>
    /// </summary>
    private static async Task<IResult> ExchangePasswordAsync(
        OpenIddictRequest request,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        SignInService signInService,
        IUserRepository users,
        IPlatformStaffRepository platformStaff,
        PlatformStaffSignInService platformStaffSignIn,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
        }

        string? tenantCode = (string?)request[TenantCodeParameter];

        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            PlatformStaffMember? candidate = await platformStaff.FindByEmailAsync(
                PlatformStaffMember.NormalizeEmailForLookup(request.Username),
                cancellationToken);

            if (candidate is not null)
            {
                return await ExchangePlatformStaffPasswordAsync(
                    request, platformStaffSignIn, platformStaff, timeProvider, cancellationToken);
            }
        }

        TenantDiscoveryResult? discovered = await discovery.DiscoverAsync(tenantCode, request.Username, cancellationToken);

        if (discovered is null || !discovered.Tenant.IsOperational)
        {
            // An unknown customer, a suspended one and a wrong password are one answer. Telling them
            // apart would let anyone discover which companies are on the platform.
            return Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
        }

        // Set before any user query: users are tenant-owned, so a lookup without a tenant finds
        // nothing and a lookup with the wrong one would find somebody else's account.
        tenantSetter.SetTenant(discovered.Tenant);

        Application.Authentication.SignInResult result =
            await signInService.SignInWithPasswordAsync(request.Username, request.Password, cancellationToken);

        if (!result.Succeeded)
        {
            // The lockout is the one distinguishable case, because a user who cannot get in for
            // fifteen minutes needs to know rather than retrying and extending it.
            return result.FailureReason == SignInFailureReason.LockedOut
                ? Forbid("account_locked", "Too many failed attempts. Try again shortly.")
                : Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
        }

        User user = await users.GetAsync(result.UserId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("The user vanished between authentication and token issuance.");

        ClaimsPrincipal principal = BuildTenantUserPrincipal(
            user,
            discovered.Tenant.Id,
            sessionId: Guid.CreateVersion7(),
            authenticatedAt: timeProvider.GetUtcNow(),
            mfaSatisfied: false,
            request.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangePlatformStaffPasswordAsync(
        OpenIddictRequest request,
        PlatformStaffSignInService signIn,
        IPlatformStaffRepository platformStaff,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        PlatformStaffSignInResult result = await signIn.SignInWithPasswordAsync(
            request.Username!,
            request.Password!,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result.FailureReason == SignInFailureReason.LockedOut
                ? Forbid("account_locked", "Too many failed attempts. Try again shortly.")
                : Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
        }

        PlatformStaffMember member = await platformStaff.GetAsync(result.PlatformStaffId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("The platform staff member vanished between authentication and token issuance.");

        ClaimsPrincipal principal = BuildPlatformStaffPrincipal(
            member,
            sessionId: Guid.CreateVersion7(),
            authenticatedAt: timeProvider.GetUtcNow(),
            request.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Trades a refresh token for a new access token.
    /// <para>
    /// The principal is rebuilt from the account record rather than copied from the old token. That
    /// is the point of refreshing at all: an account that was disabled, locked or had a role removed
    /// five minutes ago must not be handed another ten minutes of access because their old token
    /// said so. Which account to rebuild from - tenant user or platform staff - is read from the old
    /// token's own <c>utyp</c> claim, the same claim every other service uses for the same decision.
    /// </para>
    /// </summary>
    private static async Task<IResult> ExchangeRefreshTokenAsync(
        HttpContext httpContext,
        ITenantContextSetter tenantSetter,
        IUserRepository users,
        IPlatformStaffRepository platformStaff,
        ITenantRegistry registry,
        TimeProvider timeProvider,
        ILogger<SignInService> logger,
        CancellationToken cancellationToken)
    {
        AuthenticateResult authentication = await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!authentication.Succeeded || authentication.Principal is null)
        {
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        if (!Guid.TryParse(authentication.Principal.GetClaim(Claims.Subject), out Guid subjectId))
        {
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        bool isPlatformStaff = string.Equals(
            authentication.Principal.GetClaim(GggsClaims.UserType),
            GggsClaims.UserTypes.Platform,
            StringComparison.Ordinal);

        return isPlatformStaff
            ? await ExchangePlatformStaffRefreshAsync(authentication.Principal, subjectId, platformStaff, timeProvider, logger, cancellationToken)
            : await ExchangeTenantUserRefreshAsync(
                authentication.Principal, subjectId, tenantSetter, users, registry, timeProvider, logger, cancellationToken);
    }

    private static async Task<IResult> ExchangeTenantUserRefreshAsync(
        ClaimsPrincipal previous,
        Guid userId,
        ITenantContextSetter tenantSetter,
        IUserRepository users,
        ITenantRegistry registry,
        TimeProvider timeProvider,
        ILogger<SignInService> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(previous.GetClaim(GggsClaims.TenantId), out Guid tenantId))
        {
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        TenantInfo? tenant = await registry.GetAsync(tenantId, cancellationToken);

        if (tenant is null || !tenant.IsOperational)
        {
            // A tenant suspended since the token was issued. Refusing here is what makes suspension
            // bite within one token lifetime rather than one session.
            logger.LogInformation("Refresh refused for user {UserId}: the tenant is not operational.", userId);
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        tenantSetter.SetTenant(tenant);

        User? user = await users.GetAsync(userId, cancellationToken);

        if (user is null || !user.CanSignIn)
        {
            logger.LogInformation("Refresh refused for user {UserId}: the account can no longer sign in.", userId);
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        (Guid sessionId, DateTimeOffset authenticatedAt) = CarryForwardSession(previous, timeProvider);

        ClaimsPrincipal principal = BuildTenantUserPrincipal(
            user,
            tenantId,
            sessionId,
            authenticatedAt,
            mfaSatisfied: previous.HasClaim("amr", UserPrincipalFactory.AuthenticationMethods.MultiFactor),
            previous.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangePlatformStaffRefreshAsync(
        ClaimsPrincipal previous,
        Guid platformStaffId,
        IPlatformStaffRepository platformStaff,
        TimeProvider timeProvider,
        ILogger<SignInService> logger,
        CancellationToken cancellationToken)
    {
        PlatformStaffMember? member = await platformStaff.GetAsync(platformStaffId, cancellationToken);

        if (member is null || !member.CanSignIn)
        {
            logger.LogInformation(
                "Platform refresh refused for {PlatformStaffId}: the account can no longer sign in.",
                platformStaffId);
            return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
        }

        (Guid sessionId, DateTimeOffset authenticatedAt) = CarryForwardSession(previous, timeProvider);

        ClaimsPrincipal principal = BuildPlatformStaffPrincipal(member, sessionId, authenticatedAt, previous.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The original sign-in's session id and authentication time, carried forward across a refresh
    /// so an absolute session limit and a step-up check still measure from when the password was
    /// actually typed rather than resetting on every renewal.
    /// </summary>
    private static (Guid SessionId, DateTimeOffset AuthenticatedAt) CarryForwardSession(
        ClaimsPrincipal previous,
        TimeProvider timeProvider)
    {
        Guid sessionId = Guid.TryParse(previous.GetClaim(GggsClaims.SessionId), out Guid existing)
            ? existing
            : Guid.CreateVersion7();

        DateTimeOffset authenticatedAt = long.TryParse(previous.GetClaim("auth_time"), out long unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : timeProvider.GetUtcNow();

        return (sessionId, authenticatedAt);
    }

    /// <summary>
    /// Builds the principal OpenIddict turns into a token, for a tenant user.
    /// <para>
    /// The claims come from <see cref="UserPrincipalFactory"/>, which is also what cookie sign-in
    /// uses, so a service sees the same claim set whichever way the caller authenticated. What is
    /// added here is OpenIddict's own requirements, shared with the platform staff path below.
    /// </para>
    /// </summary>
    private static ClaimsPrincipal BuildTenantUserPrincipal(
        User user,
        Guid tenantId,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        bool mfaSatisfied,
        IEnumerable<string> requestedScopes)
    {
        ClaimsPrincipal principal = UserPrincipalFactory.Create(
            user,
            tenantId,
            sessionId,
            authenticatedAt,
            mfaSatisfied,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return ApplyOpenIddictProperties(principal, user.Id, requestedScopes);
    }

    /// <summary>The platform staff equivalent of <see cref="BuildTenantUserPrincipal"/>. No tenant, no MFA state to carry.</summary>
    private static ClaimsPrincipal BuildPlatformStaffPrincipal(
        PlatformStaffMember member,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        IEnumerable<string> requestedScopes)
    {
        ClaimsPrincipal principal = PlatformStaffPrincipalFactory.Create(
            member,
            sessionId,
            authenticatedAt,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return ApplyOpenIddictProperties(principal, member.Id, requestedScopes);
    }

    /// <summary>
    /// OpenIddict's own requirements, common to both account types: the subject claim it keys
    /// tokens by, the scopes, the resource the token is for, and a destination for every claim.
    /// </summary>
    private static ClaimsPrincipal ApplyOpenIddictProperties(
        ClaimsPrincipal principal,
        Guid subjectId,
        IEnumerable<string> requestedScopes)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.SetClaim(Claims.Subject, subjectId.ToString());

        // Only the scope we serve. An unrecognised scope is dropped rather than honoured, so a client
        // cannot widen its own token by asking.
        principal.SetScopes(requestedScopes.Intersect([ApiScope, Scopes.OfflineAccess], StringComparer.Ordinal));

        // Becomes the audience. Every service validates it, so a token minted for this product is not
        // accepted by anything else that happens to trust our signing key.
        principal.SetResources(ApiScope);

        // OpenIddict puts nothing in a token unless told where it belongs. Everything here goes in
        // the access token and nothing in the identity token: the identity token is for the client,
        // and the client is our own gateway, which reads the session from the API instead.
        principal.SetDestinations(_ => [Destinations.AccessToken]);

        return principal;
    }

    /// <summary>
    /// The error shape the OAuth specification requires, which is not our usual ProblemDetails.
    /// <para>
    /// Deliberately so: a client library parses <c>error</c> and <c>error_description</c>, and a
    /// token endpoint answering in a house style is a token endpoint no standard client can read.
    /// </para>
    /// </summary>
    private static IResult Forbid(string error, string description)
        => Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
