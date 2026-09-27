using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Application.Authentication;
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
/// </summary>
public static class TokenEndpoints
{
    /// <summary>
    /// The tenant hint, passed alongside the credentials.
    /// <para>
    /// A hint about <em>where to look</em>, not an assertion about who the user is: it selects which
    /// customer's directory is searched, and the tenant the token is issued for still comes from the
    /// user record that search finds.
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
                request, discovery, tenantSetter, signInService, users, timeProvider, cancellationToken);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await ExchangeRefreshTokenAsync(
                httpContext, tenantSetter, users, registry, timeProvider, logger, cancellationToken);
        }

        return Forbid(Errors.UnsupportedGrantType, "That grant type is not supported.");
    }

    private static async Task<IResult> ExchangePasswordAsync(
        OpenIddictRequest request,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        SignInService signInService,
        IUserRepository users,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
        }

        TenantDiscoveryResult? discovered = await discovery.DiscoverAsync(
            (string?)request[TenantCodeParameter],
            request.Username,
            cancellationToken);

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

        ClaimsPrincipal principal = BuildPrincipal(
            user,
            discovered.Tenant.Id,
            sessionId: Guid.CreateVersion7(),
            authenticatedAt: timeProvider.GetUtcNow(),
            mfaSatisfied: false,
            request.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Trades a refresh token for a new access token.
    /// <para>
    /// The principal is rebuilt from the user record rather than copied from the old token. That is
    /// the point of refreshing at all: a user who was disabled, locked or had a role removed five
    /// minutes ago must not be handed another ten minutes of access because their old token said so.
    /// </para>
    /// </summary>
    private static async Task<IResult> ExchangeRefreshTokenAsync(
        HttpContext httpContext,
        ITenantContextSetter tenantSetter,
        IUserRepository users,
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

        if (!Guid.TryParse(authentication.Principal.GetClaim(Claims.Subject), out Guid userId)
            || !Guid.TryParse(authentication.Principal.GetClaim(GggsClaims.TenantId), out Guid tenantId))
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

        // The original sign-in's session and authentication time are carried forward, so an absolute
        // session limit and a step-up check still measure from when the password was actually typed.
        Guid sessionId = Guid.TryParse(authentication.Principal.GetClaim(GggsClaims.SessionId), out Guid existing)
            ? existing
            : Guid.CreateVersion7();

        DateTimeOffset authenticatedAt = long.TryParse(
            authentication.Principal.GetClaim("auth_time"),
            out long unixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                : timeProvider.GetUtcNow();

        ClaimsPrincipal principal = BuildPrincipal(
            user,
            tenantId,
            sessionId,
            authenticatedAt,
            mfaSatisfied: authentication.Principal.HasClaim(
                "amr",
                UserPrincipalFactory.AuthenticationMethods.MultiFactor),
            authentication.Principal.GetScopes());

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Builds the principal OpenIddict turns into a token.
    /// <para>
    /// The claims come from <see cref="UserPrincipalFactory"/>, which is also what cookie sign-in
    /// uses, so a service sees the same claim set whichever way the caller authenticated. What is
    /// added here is OpenIddict's own requirements: the subject claim it keys tokens by, the scopes,
    /// the resource the token is for, and a destination for every claim.
    /// </para>
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(
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

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.SetClaim(Claims.Subject, user.Id.ToString());

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
