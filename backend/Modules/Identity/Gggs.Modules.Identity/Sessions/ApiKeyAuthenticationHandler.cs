using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.Modules.Identity.Application.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Identity.Sessions;

/// <summary>
/// Authenticates a request from its <c>X-Api-Key</c> header: an outside system calling the API as
/// the key's machine account. A bad key is a plain 401, the same whether it is malformed, unknown,
/// revoked or expired, so the answer gives nothing away.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return AuthenticateResult.NoResult();
        }

        ApiKeyService keys = Context.RequestServices.GetRequiredService<ApiKeyService>();
        ClaimsPrincipal? principal = await keys.AuthenticateAsync(values.ToString(), Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail("The API key is not valid.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
