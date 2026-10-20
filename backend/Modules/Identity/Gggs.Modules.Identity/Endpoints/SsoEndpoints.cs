using System.Net;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>
/// Single sign-on: the sign-in page's provider buttons, the round trip through the provider, and the
/// administration of an organisation's providers.
/// </summary>
public static class SsoEndpoints
{
    /// <summary>Carries the sealed state through the provider. Lax, because the return is a cross-site navigation.</summary>
    public const string StateCookie = "__Host-gggs-sso";

    private const string SignInPage = "/sign-in";

    public static IEndpointRouteBuilder MapSsoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder bff = endpoints.MapGroup("/bff/sso").WithTags("Session");
        bff.MapGet("/providers", async (string? tenant, SsoService sso, CancellationToken ct) => TypedResults.Ok(await sso.OptionsAsync(tenant, ct)))
            .WithName("SsoProviders").AllowAnonymous();
        bff.MapGet("/start", StartAsync).WithName("SsoStart").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);
        bff.MapGet("/callback", CallbackAsync).WithName("SsoCallback").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);

        RouteGroupBuilder admin = endpoints.MapGroup("/api/v1/admin/identity-providers").WithTags("Security");
        admin.MapGet("/", async (SsoService sso, CancellationToken ct) => TypedResults.Ok(await sso.ListAsync(ct)))
            .WithName("ListSsoProviders").RequirePermission(Permissions.Admin.IdentityProviderManage);
        admin.MapPost("/", async (SsoProviderRequest body, SsoService sso, CancellationToken ct) => TypedResults.Ok(await sso.CreateAsync(body, ct)))
            .WithName("CreateSsoProvider").RequirePermission(Permissions.Admin.IdentityProviderManage);
        admin.MapPut("/{id:guid}", async (Guid id, SsoProviderRequest body, SsoService sso, CancellationToken ct) => TypedResults.Ok(await sso.UpdateAsync(id, body, ct)))
            .WithName("UpdateSsoProvider").RequirePermission(Permissions.Admin.IdentityProviderManage);
        admin.MapDelete("/{id:guid}", async (Guid id, SsoService sso, CancellationToken ct) =>
            {
                await sso.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteSsoProvider").RequirePermission(Permissions.Admin.IdentityProviderManage);

        return endpoints;
    }

    private static async Task<RedirectHttpResult> StartAsync(
        string? tenant,
        string? provider,
        string? returnUrl,
        HttpContext context,
        SsoService sso,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        string origin = configuration["Gggs:Sso:PublicOrigin"]?.TrimEnd('/') ?? $"{context.Request.Scheme}://{context.Request.Host}";
        SsoStart start;
        try
        {
            start = await sso.StartAsync(tenant, provider, $"{origin}/bff/sso/callback", returnUrl, cancellationToken);
        }
        catch (Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException exception)
        {
            return TypedResults.Redirect($"{SignInPage}?ssoError={Uri.EscapeDataString(exception.ErrorCode)}");
        }

        context.Response.Cookies.Append(StateCookie, start.State, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10),
        });
        return TypedResults.Redirect(start.AuthorizationUrl);
    }

    /// <summary>
    /// The provider's return. It answers with a page that moves on by itself rather than a redirect:
    /// the session cookie is SameSite=Strict, and a browser withholds it for the rest of a redirect
    /// chain that began on another site - the first page after sign-in would look signed out.
    /// </summary>
    private static async Task<ContentHttpResult> CallbackAsync(
        string? state,
        string? code,
        string? error,
        HttpContext context,
        SsoService sso,
        BffSessionStore sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? sealedState = context.Request.Cookies[StateCookie];
        context.Response.Cookies.Delete(StateCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });

        if (context.Features.Get<BffSessionFeature>() is { } existing)
        {
            await sessions.RemoveAsync(existing.SessionKey, cancellationToken);
        }

        SsoCompletion completion = await sso.CompleteAsync(sealedState, state, code, error, SessionAuthenticationHandler.SchemeName, cancellationToken);
        BffLoginResponse response = await BrowserSessionEndpoints.StartSessionAsync(completion.Outcome, context, sessions, cancellationToken);

        string next = response switch
        {
            { Succeeded: true } => completion.ReturnUrl,
            { MfaChallenge: { } challenge } => $"{SignInPage}?mfaChallenge={Uri.EscapeDataString(challenge)}",
            _ => $"{SignInPage}?ssoError={Uri.EscapeDataString(response.ErrorCode ?? SignInErrorCodes.InvalidCredentials)}",
        };

        string target = WebUtility.HtmlEncode(next);
        return TypedResults.Content(
            $"<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0;url={target}\"><title>Signing in</title></head><body><p><a href=\"{target}\">Continue</a></p></body></html>",
            "text/html");
    }
}
