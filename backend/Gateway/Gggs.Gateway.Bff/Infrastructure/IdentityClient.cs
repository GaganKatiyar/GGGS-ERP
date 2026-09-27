using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Gggs.Gateway.Bff.Infrastructure;

/// <summary>What the gateway sends to Identity to sign someone in.</summary>
public sealed record SignInUpstreamRequest(string Email, string Password, string? TenantCode);

/// <summary>Identity's answer, plus the credential it issued when the answer was yes.</summary>
public sealed record SignInUpstreamResult(
    bool Succeeded,
    string? ErrorCode,
    bool MustChangePassword,
    DateTime? LockoutEndDate,
    string? Credential);

/// <summary>
/// The gateway's one conversation with Identity.
/// <para>
/// Kept in a single class because it is the only place a credential is handled, and a credential
/// handled in several places is a credential that leaks in one of them.
/// </para>
/// <para>
/// The credential is Identity's own session cookie today. When the OIDC server ships this becomes an
/// authorization-code exchange returning an access and refresh token, and the shape of everything
/// around it - store the credential, attach it on the way out, never send it to the browser - does
/// not change. That is why this is a seam rather than inline code in the endpoint.
/// </para>
/// </summary>
public sealed class IdentityClient(HttpClient httpClient, ILogger<IdentityClient> logger)
{
    /// <summary>The cookie Identity issues. Matched by name so a second cookie cannot be mistaken for it.</summary>
    private const string IdentityCookieName = "__Host-gggs-identity";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<SignInUpstreamResult> SignInAsync(
        SignInUpstreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            request,
            SerializerOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Identity answers 200 with Succeeded=false for a wrong password, so a non-success
            // status means the service itself is unwell rather than the credential being wrong.
            logger.LogError("Identity returned {StatusCode} for a sign-in attempt.", (int)response.StatusCode);
            return new SignInUpstreamResult(false, "SIGN_IN_UNAVAILABLE", false, null, null);
        }

        SignInUpstreamBody? body = await response.Content.ReadFromJsonAsync<SignInUpstreamBody>(
            SerializerOptions,
            cancellationToken);

        if (body is null || !body.Succeeded)
        {
            return new SignInUpstreamResult(
                false,
                body?.ErrorCode ?? "INVALID_CREDENTIALS",
                false,
                body?.LockoutEndDate,
                null);
        }

        string? credential = ExtractSessionCookie(response);

        if (credential is null)
        {
            // Succeeded but issued nothing to carry forward: a contract break, not a user error.
            logger.LogError("Identity reported a successful sign-in without issuing a session cookie.");
            return new SignInUpstreamResult(false, "SIGN_IN_UNAVAILABLE", false, null, null);
        }

        return new SignInUpstreamResult(true, null, body.MustChangePassword, null, credential);
    }

    /// <summary>Reads the session the credential belongs to, which is what <c>/bff/user</c> returns.</summary>
    public async Task<JsonElement?> GetSessionAsync(string credential, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/session");
        Attach(request, credential);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Identity returned {StatusCode} for a session read.", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Ends the upstream session. Best effort: the gateway deletes its own record either way, so a
    /// failure here leaves an orphaned session upstream rather than a user still signed in here.
    /// </summary>
    public async Task SignOutAsync(string credential, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out");
            Attach(request, credential);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Upstream sign-out failed; the local session has still been ended.");
        }
    }

    /// <summary>Attaches the stored credential to an outbound request.</summary>
    public static void Attach(HttpRequestMessage request, string credential)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Add("Cookie", $"{IdentityCookieName}={credential}");
    }

    /// <summary>The header name and value the proxy attaches, for requests it forwards itself.</summary>
    public static (string Name, string Value) CredentialHeader(string credential)
        => ("Cookie", $"{IdentityCookieName}={credential}");

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            return null;
        }

        foreach (string setCookie in setCookies)
        {
            // Parsed rather than split on ';' by hand, so attributes with embedded separators do not
            // produce a truncated credential that fails mysteriously later.
            if (!SetCookieHeaderValue.TryParse(setCookie, out SetCookieHeaderValue? parsed))
            {
                continue;
            }

            if (string.Equals(parsed.Name.Value, IdentityCookieName, StringComparison.Ordinal))
            {
                return parsed.Value.Value;
            }
        }

        return null;
    }

    private sealed record SignInUpstreamBody(
        bool Succeeded,
        string? ErrorCode,
        bool MustChangePassword,
        DateTime? LockoutEndDate);
}
