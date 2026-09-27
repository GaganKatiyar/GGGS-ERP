using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gggs.BuildingBlocks.Application;
using Microsoft.Extensions.Options;

namespace Gggs.Gateway.Bff.Infrastructure;

/// <summary>Where the gateway's own client credentials come from.</summary>
public sealed class IdentityClientOptions
{
    public const string SectionName = "Gggs:Bff:Identity";

    /// <summary>Must match the client registered by the Identity service.</summary>
    public string ClientId { get; set; } = "gggs-bff";

    /// <summary>
    /// The gateway's secret.
    /// <para>
    /// This is what makes the gateway a confidential client. Without it anyone who can reach the token
    /// endpoint could use the password grant themselves, at their own pace, against any account.
    /// </para>
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>A successful token exchange, or the reason it failed.</summary>
public sealed record TokenResult
{
    public required bool Succeeded { get; init; }

    /// <summary>Stable code the UI switches on, never the message text.</summary>
    public string? ErrorCode { get; init; }

    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset AccessTokenExpiresAt { get; init; }
}

/// <summary>
/// The gateway's one conversation with Identity.
/// <para>
/// Kept in a single class because it is the only place a credential is handled, and a credential
/// handled in several places is a credential that leaks in one of them.
/// </para>
/// <para>
/// The exchange is the OAuth password grant, which is right here and would be wrong almost anywhere
/// else. It is available only to this first-party confidential client, which has just collected the
/// credential from its own sign-in page, so nothing is being delegated to a third party. The
/// alternative - an authorization-code redirect - needs Identity to host its own login UI, which is
/// work that only pays for itself once there is a federated provider to redirect to. When that lands,
/// this method is what changes; nothing downstream of it is.
/// </para>
/// </summary>
public sealed class IdentityClient(
    HttpClient httpClient,
    IOptions<IdentityClientOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityClient> logger)
{
    /// <summary>
    /// The scope asked for. <c>offline_access</c> is what makes Identity issue a refresh token, and
    /// without it a session would end after ten minutes.
    /// </summary>
    private const string Scope = "gggs.api offline_access";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IdentityClientOptions _options = options.Value;

    /// <summary>
    /// Falls back to the shared development placeholder, which is the same value the Identity service
    /// registers the client with. Both hosts refuse to start outside Development without a real one.
    /// </summary>
    private string ClientSecret => string.IsNullOrWhiteSpace(_options.ClientSecret)
        ? DevelopmentSecrets.GatewayClientSecret
        : _options.ClientSecret;

    /// <summary>Exchanges a password for tokens.</summary>
    public async Task<TokenResult> SignInAsync(
        string email,
        string password,
        string? tenantCode,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = password,
            ["scope"] = Scope,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = ClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            form["tenant_code"] = tenantCode;
        }

        return await ExchangeAsync(form, cancellationToken);
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token.
    /// <para>
    /// The refresh token is one-time: the response carries a new one, and the old one is dead. A
    /// caller that loses the response loses the session, which is the deliberate cost of being able
    /// to detect a stolen token being replayed.
    /// </para>
    /// </summary>
    public async Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        => await ExchangeAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = ClientSecret,
            },
            cancellationToken);

    /// <summary>Reads the session the token belongs to, which is what <c>/bff/user</c> returns.</summary>
    public async Task<JsonElement?> GetSessionAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Identity returned {StatusCode} for a session read.", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Revokes the refresh token so the session cannot be resumed.
    /// <para>
    /// Best effort: the gateway deletes its own record either way, so a failure here leaves a
    /// revocable token upstream rather than a user still signed in. The access token is not revoked
    /// because it cannot be - that is what its ten-minute lifetime is for.
    /// </para>
    /// </summary>
    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = ClientSecret,
            });

            using HttpResponseMessage response = await httpClient.PostAsync(
                "/connect/revoke",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Token revocation returned {StatusCode}; the local session has still been ended.",
                    (int)response.StatusCode);
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Token revocation failed; the local session has still been ended.");
        }
    }

    private async Task<TokenResult> ExchangeAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);

        using HttpResponseMessage response = await httpClient.PostAsync("/connect/token", content, cancellationToken);

        TokenEndpointBody? body = await response.Content.ReadFromJsonAsync<TokenEndpointBody>(
            SerializerOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode || body?.AccessToken is null)
        {
            // OAuth error codes, not ours. Mapped here so nothing above this line has to know the
            // difference between invalid_grant and a lockout.
            string errorCode = body?.Error switch
            {
                "account_locked" => "ACCOUNT_LOCKED",
                "invalid_grant" => "INVALID_CREDENTIALS",
                null => "SIGN_IN_UNAVAILABLE",
                _ => "INVALID_CREDENTIALS",
            };

            if (errorCode == "SIGN_IN_UNAVAILABLE")
            {
                logger.LogError(
                    "The token endpoint returned {StatusCode} with no OAuth error code.",
                    (int)response.StatusCode);
            }

            return new TokenResult { Succeeded = false, ErrorCode = errorCode };
        }

        return new TokenResult
        {
            Succeeded = true,
            AccessToken = body.AccessToken,
            RefreshToken = body.RefreshToken,

            // Computed from expires_in rather than read from the token: the gateway never parses a
            // token it does not need to read, and the server's own idea of the lifetime is the one
            // that matters.
            AccessTokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(body.ExpiresIn),
        };
    }

    private sealed record TokenEndpointBody
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
