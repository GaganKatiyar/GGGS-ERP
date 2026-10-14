using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gggs.Modules.Identity.Endpoints;

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
/// Two grants are supported, for non-browser clients (integrations, mobile, scripts): <c>password</c>
/// and <c>refresh_token</c>. The browser does not use this endpoint - it signs in through
/// <c>/bff/login</c> and holds only a session cookie - but both paths share
/// <see cref="PasswordSignInService"/> and <see cref="PrincipalRevalidator"/>, so they cannot
/// disagree about who a credential belongs to. Nothing else is enabled, so there is no authorization-code flow to misconfigure until
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
        PasswordSignInService passwordSignIn,
        PrincipalRevalidator revalidator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request could not be read from the context.");

        if (request.IsPasswordGrantType())
        {
            PasswordSignInOutcome outcome = await passwordSignIn.SignInAsync(
                request.Username ?? string.Empty,
                request.Password ?? string.Empty,
                (string?)request[TenantCodeParameter],
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return outcome.ErrorCode == SignInErrorCodes.AccountLocked
                    ? Forbid("account_locked", "Too many failed attempts. Try again shortly.")
                    : Forbid(Errors.InvalidGrant, "The credentials supplied are not valid.");
            }

            return SignIn(outcome.Principal!, outcome.SubjectId!.Value, request.GetScopes());
        }

        if (request.IsRefreshTokenGrantType())
        {
            AuthenticateResult authentication = await httpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            if (!authentication.Succeeded || authentication.Principal is null)
            {
                return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
            }

            // Rebuilt from the account record, not copied from the old token: that is the point of
            // refreshing at all.
            ClaimsPrincipal? principal = await revalidator.RebuildAsync(
                authentication.Principal,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                cancellationToken);

            if (principal is null
                || !Guid.TryParse(principal.FindFirstValue(Claims.Subject), out Guid subjectId))
            {
                return Forbid(Errors.InvalidGrant, "That refresh token is no longer valid.");
            }

            return SignIn(principal, subjectId, authentication.Principal.GetScopes());
        }

        return Forbid(Errors.UnsupportedGrantType, "That grant type is not supported.");
    }

    private static IResult SignIn(ClaimsPrincipal principal, Guid subjectId, IEnumerable<string> requestedScopes)
        => Results.SignIn(
            ApplyOpenIddictProperties(principal, subjectId, requestedScopes),
            properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

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
