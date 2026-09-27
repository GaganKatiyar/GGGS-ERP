using System.Text.Json;
using Gggs.Gateway.Bff.Infrastructure;
using Gggs.Gateway.Bff.Session;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Gateway.Bff.Endpoints;

/// <summary>Credentials as the sign-in page sends them.</summary>
public sealed record BffLoginRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    /// <summary>
    /// Which customer to look in. Usually filled from the subdomain; optional for customers who have
    /// verified an email domain.
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

    /// <summary>
    /// Stable code the UI switches on, never the message text.
    /// <para>
    /// <c>INVALID_CREDENTIALS</c> covers a wrong password, an unknown address and a tenant that does
    /// not exist, deliberately. <c>ACCOUNT_LOCKED</c> is the one distinguishable case, because a user
    /// who cannot get in for fifteen minutes needs to know rather than retrying and extending it.
    /// </para>
    /// <para>
    /// There is no "must change password" or lockout-until field here. The OAuth token endpoint has no
    /// room for either, and inventing a channel for them would mean a second sign-in path to keep
    /// correct. They belong on the session document, where the client already looks.
    /// </para>
    /// </summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// The browser's session endpoints.
/// <para>
/// These four routes are the whole reason the gateway exists. Everything else it does is proxying;
/// this is where a credential is exchanged for a cookie and never crosses back.
/// </para>
/// </summary>
public static class BffAuthEndpoints
{
    public static IEndpointRouteBuilder MapBffAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/bff").WithTags("Session");

        group.MapPost("/login", LoginAsync).WithName("BffLogin");
        group.MapPost("/logout", LogoutAsync).WithName("BffLogout");
        group.MapGet("/user", GetUserAsync).WithName("BffUser");

        return endpoints;
    }

    private static async Task<Ok<BffLoginResponse>> LoginAsync(
        BffLoginRequest body,
        HttpContext context,
        IdentityClient identity,
        BffSessionStore sessions,
        ILogger<BffLoginResponse> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(context);

        // An existing session is discarded first. Signing in while already signed in otherwise
        // leaves the previous session alive and revocable by nobody.
        string? existing = context.GetSessionKey();
        if (existing is not null)
        {
            await sessions.RemoveAsync(existing, cancellationToken);
        }

        TokenResult result = await identity.SignInAsync(
            body.Email,
            body.Password,
            body.TenantCode,
            cancellationToken);

        if (!result.Succeeded || result.AccessToken is null || result.RefreshToken is null)
        {
            // No detail beyond the code, and the same code whether the address is unknown or the
            // password is wrong: anything finer is a way to find out who has an account here.
            return TypedResults.Ok(new BffLoginResponse
            {
                Succeeded = false,
                ErrorCode = result.ErrorCode ?? "INVALID_CREDENTIALS",
            });
        }

        JsonElement? session = await identity.GetSessionAsync(result.AccessToken, cancellationToken);

        if (session is null)
        {
            logger.LogError("A sign-in succeeded but the session could not be read back from Identity.");

            return TypedResults.Ok(new BffLoginResponse
            {
                Succeeded = false,
                ErrorCode = "SIGN_IN_UNAVAILABLE",
            });
        }

        string sessionKey = await sessions.CreateAsync(
            ReadGuid(session.Value, "userId") ?? Guid.Empty,
            ReadTenantId(session.Value),
            result.AccessToken,
            result.RefreshToken,
            result.AccessTokenExpiresAt,
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        SessionResolutionMiddleware.SetCookie(context, sessionKey);

        return TypedResults.Ok(new BffLoginResponse { Succeeded = true });
    }

    private static async Task<NoContent> LogoutAsync(
        HttpContext context,
        IdentityClient identity,
        BffSessionStore sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        BffSession? session = context.GetSession();
        string? sessionKey = context.GetSessionKey();

        if (session is not null)
        {
            await identity.SignOutAsync(session.RefreshToken, cancellationToken);
        }

        if (sessionKey is not null)
        {
            await sessions.RemoveAsync(sessionKey, cancellationToken);
        }

        // Cleared even when there was no session: a browser holding a key we do not recognise should
        // stop sending it.
        SessionResolutionMiddleware.ClearCookie(context);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The signed-in user, or 401.
    /// <para>
    /// Read through on every call rather than cached in the session record, so a role change or a
    /// suspended tenant is reflected on the next page load instead of whenever the session happens
    /// to end.
    /// </para>
    /// </summary>
    private static async Task<Results<Ok<JsonElement>, UnauthorizedHttpResult>> GetUserAsync(
        HttpContext context,
        IdentityClient identity,
        BffSessionStore sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        BffSession? session = context.GetSession();

        if (session is null)
        {
            // Not an error: this is exactly how an anonymous visitor looks, and the SPA calls it on
            // every load precisely to find out.
            return TypedResults.Unauthorized();
        }

        JsonElement? upstream = await identity.GetSessionAsync(session.AccessToken, cancellationToken);

        if (upstream is null)
        {
            // The upstream credential has stopped working - expired, or the user was disabled. The
            // local session is ended too, rather than left pointing at something dead.
            string? sessionKey = context.GetSessionKey();

            if (sessionKey is not null)
            {
                await sessions.RemoveAsync(sessionKey, cancellationToken);
            }

            SessionResolutionMiddleware.ClearCookie(context);
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(upstream.Value);
    }

    private static Guid? ReadGuid(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out Guid parsed)
                ? parsed
                : null;

    private static Guid? ReadTenantId(JsonElement session)
        => session.TryGetProperty("tenant", out JsonElement tenant) && tenant.ValueKind == JsonValueKind.Object
            ? ReadGuid(tenant, "id")
            : null;
}
