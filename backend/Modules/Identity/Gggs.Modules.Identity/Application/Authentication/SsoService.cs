using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Sso;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gggs.Modules.Identity.Application.Authentication;

public sealed record SsoProviderOption(string Key, string DisplayName);

public sealed record SsoProviderView(Guid Id, string Key, string DisplayName, string Authority, string ClientId, bool HasClientSecret, string Scopes, string? AllowedDomains, bool Enabled);

/// <summary>Create or change a provider. A null secret leaves the stored one alone; an empty one removes it.</summary>
public sealed record SsoProviderRequest(string Key, string DisplayName, string Authority, string ClientId, string? ClientSecret, string? Scopes, string? AllowedDomains, bool Enabled);

/// <summary>Where to send the browser, and the sealed state it must bring back.</summary>
public sealed record SsoStart(string AuthorizationUrl, string State);

/// <summary>The end of a federated sign-in, and where the person was heading.</summary>
public sealed record SsoCompletion(PasswordSignInOutcome Outcome, string ReturnUrl);

/// <summary>
/// Single sign-on through an organisation's own OpenID Connect provider: the authorization-code flow
/// with PKCE, state and nonce, the id_token checked against the provider's published keys, and the
/// person matched to an existing account by verified email address.
/// <para>
/// Everything the browser carries between leaving and coming back - state, nonce, PKCE verifier,
/// tenant, return address - travels sealed in a short-lived cookie, so nothing is kept server-side
/// for a sign-in that is never finished.
/// </para>
/// </summary>
public sealed class SsoService(
    AppDbContext db,
    ITenantRegistry tenants,
    ITenantContextSetter tenantSetter,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ISensitiveDataProtector protector,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpClients,
    MfaService mfa,
    TimeProvider timeProvider,
    ILogger<SsoService> logger)
{
    public const string HttpClientName = "sso";
    public const string SecretPurpose = "identity.sso-client-secret";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private ITimeLimitedDataProtector States => dataProtection.CreateProtector("Gggs.Identity.SsoState.v1").ToTimeLimitedDataProtector();

    // ----- Administration (signed in, tenant set) ------------------------------------------------

    public async Task<IReadOnlyList<SsoProviderView>> ListAsync(CancellationToken cancellationToken)
        => [.. (await db.SsoProviders.AsNoTracking().OrderBy(provider => provider.DisplayName).ToListAsync(cancellationToken)).Select(View)];

    public async Task<SsoProviderView> CreateAsync(SsoProviderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = SsoProvider.Create(request.Key, request.DisplayName, request.Authority, request.ClientId, request.Scopes, request.AllowedDomains, request.Enabled);
        if (await db.SsoProviders.AnyAsync(existing => existing.Key == provider.Key, cancellationToken))
        {
            throw new DomainRuleException("SSO_KEY_TAKEN", $"There is already a provider with the key '{provider.Key}'.");
        }

        SetSecret(provider, request.ClientSecret);
        db.SsoProviders.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return View(provider);
    }

    public async Task<SsoProviderView> UpdateAsync(Guid id, SsoProviderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SsoProvider provider = await FindAsync(id, cancellationToken);
        provider.Update(request.DisplayName, request.Authority, request.ClientId, request.Scopes, request.AllowedDomains, request.Enabled);
        SetSecret(provider, request.ClientSecret);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return View(provider);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        db.SsoProviders.Remove(await FindAsync(id, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ----- Sign-in (anonymous) -------------------------------------------------------------------

    /// <summary>The buttons the sign-in page shows for an organisation; none for an unknown one.</summary>
    public async Task<IReadOnlyList<SsoProviderOption>> OptionsAsync(string? tenantCode, CancellationToken cancellationToken)
    {
        if (await EnterTenantAsync(tenantCode, cancellationToken) is null)
        {
            return [];
        }

        return await db.SsoProviders.AsNoTracking()
            .Where(provider => provider.Enabled)
            .OrderBy(provider => provider.DisplayName)
            .Select(provider => new SsoProviderOption(provider.Key, provider.DisplayName))
            .ToListAsync(cancellationToken);
    }

    public async Task<SsoStart> StartAsync(string? tenantCode, string? providerKey, string redirectUri, string? returnUrl, CancellationToken cancellationToken)
    {
        TenantInfo tenant = await EnterTenantAsync(tenantCode, cancellationToken) ?? throw Unknown();
        string key = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
        SsoProvider provider = await db.SsoProviders.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Key == key && candidate.Enabled, cancellationToken) ?? throw Unknown();
        Discovery discovery = await DiscoverAsync(provider, cancellationToken);

        var state = new SealedState(tenant.Id, provider.Id, Random(), Random(), Random(), SafeReturnUrl(returnUrl), redirectUri);
        string challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(state.Verifier)));
        string url = QueryHelpers.AddQueryString(discovery.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = provider.Scopes,
            ["state"] = state.State,
            ["nonce"] = state.Nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        return new SsoStart(url, States.Protect(JsonSerializer.Serialize(state), StateLifetime));
    }

    /// <summary>Finishes the sign-in when the provider sends the browser back.</summary>
    public async Task<SsoCompletion> CompleteAsync(string? sealedState, string? state, string? code, string? error, string authenticationScheme, CancellationToken cancellationToken)
    {
        SealedState? saved = Unseal(sealedState);
        if (saved is null || string.IsNullOrEmpty(state) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(saved.State), Encoding.ASCII.GetBytes(state)))
        {
            return new SsoCompletion(PasswordSignInOutcome.Failed("SSO_STATE_INVALID"), "/");
        }

        string returnUrl = saved.ReturnUrl;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            logger.LogInformation("The identity provider ended a sign-in without a code: {Error}.", error);
            return new SsoCompletion(PasswordSignInOutcome.Failed("SSO_CANCELLED"), returnUrl);
        }

        if (await tenants.GetAsync(saved.TenantId, cancellationToken) is not { IsOperational: true } tenant)
        {
            return new SsoCompletion(PasswordSignInOutcome.Failed("SSO_STATE_INVALID"), returnUrl);
        }

        tenantSetter.SetTenant(tenant);
        if (await db.SsoProviders.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == saved.ProviderId && candidate.Enabled, cancellationToken) is not { } provider)
        {
            return new SsoCompletion(PasswordSignInOutcome.Failed("SSO_PROVIDER_UNKNOWN"), returnUrl);
        }

        ClaimsIdentity? identity = await RedeemAsync(provider, saved, code, cancellationToken);
        if (identity is null)
        {
            return new SsoCompletion(PasswordSignInOutcome.Failed("SSO_TOKEN_INVALID"), returnUrl);
        }

        return new SsoCompletion(await SignInAsync(provider, tenant, identity, authenticationScheme, cancellationToken), returnUrl);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<PasswordSignInOutcome> SignInAsync(SsoProvider provider, TenantInfo tenant, ClaimsIdentity identity, string authenticationScheme, CancellationToken cancellationToken)
    {
        string? subject = identity.FindFirst("sub")?.Value;
        string? email = identity.FindFirst("email")?.Value ?? identity.FindFirst("preferred_username")?.Value ?? identity.FindFirst("upn")?.Value;
        bool unverified = string.Equals(identity.FindFirst("email_verified")?.Value, "false", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal) || unverified || !provider.AllowsEmail(email))
        {
            logger.LogInformation("Single sign-on through {Provider} refused: no usable verified email address.", provider.Key);
            return PasswordSignInOutcome.Failed("SSO_NO_ACCOUNT");
        }

        User? user = await users.FindByEmailAsync(User.NormalizeEmailForLookup(email), cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (user is null || user.Status == UserStatus.Disabled)
        {
            logger.LogInformation("Single sign-on through {Provider} refused: no active account for that address.", provider.Key);
            return PasswordSignInOutcome.Failed("SSO_NO_ACCOUNT");
        }

        // Once linked, the account follows the provider's subject, not whoever later holds the address.
        if (user.ExternalLogins.FirstOrDefault(login => string.Equals(login.Provider, provider.LoginProvider, StringComparison.OrdinalIgnoreCase)) is { } linked
            && !string.Equals(linked.ProviderKey, subject, StringComparison.Ordinal))
        {
            logger.LogWarning("Single sign-on through {Provider} refused for user {UserId}: a different identity is linked.", provider.Key, user.Id);
            return PasswordSignInOutcome.Failed("SSO_ACCOUNT_MISMATCH");
        }

        if (user.IsLockedOut(now))
        {
            return PasswordSignInOutcome.Failed(SignInErrorCodes.AccountLocked, user.LockoutEndDate);
        }

        user.ActivateThroughFederation();
        user.LinkExternalLogin(provider.LoginProvider, subject, identity.FindFirst("name")?.Value, now);

        // The provider vouches for its own second factor; a local authenticator still applies when it does not.
        bool providerMfa = identity.FindAll("amr").Any(claim => claim.Value is "mfa" or "otp" or "hwk" or "swk" or "fido");
        if (user.MfaEnabled && !providerMfa)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new PasswordSignInOutcome
            {
                Succeeded = false,
                ErrorCode = SignInErrorCodes.MfaRequired,
                SubjectId = user.Id,
                MfaChallenge = mfa.IssueChallenge(user.Id, tenant.Id),
            };
        }

        user.RecordSuccessfulSignIn(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("User {UserId} signed in through {Provider}.", user.Id, provider.Key);
        return new PasswordSignInOutcome
        {
            Succeeded = true,
            SubjectId = user.Id,
            Principal = UserPrincipalFactory.Create(user, tenant.Id, Guid.CreateVersion7(), timeProvider.GetUtcNow(), providerMfa, authenticationScheme, UserPrincipalFactory.AuthenticationMethods.ExternalProvider),
        };
    }

    /// <summary>Swaps the code for tokens and checks the id_token: signature, issuer, audience, lifetime and nonce.</summary>
    private async Task<ClaimsIdentity?> RedeemAsync(SsoProvider provider, SealedState saved, string code, CancellationToken cancellationToken)
    {
        try
        {
            Discovery discovery = await DiscoverAsync(provider, cancellationToken);
            HttpClient http = httpClients.CreateClient(HttpClientName);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = saved.RedirectUri,
                ["client_id"] = provider.ClientId,
                ["code_verifier"] = saved.Verifier,
            };
            if (provider.ClientSecret is { } secret)
            {
                form["client_secret"] = protector.Reveal(SecretPurpose, new ProtectedValue(secret, "-"));
            }

            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await http.PostAsync(new Uri(discovery.TokenEndpoint), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The token endpoint of {Provider} answered {Status}.", provider.Key, (int)response.StatusCode);
                return null;
            }

            using JsonDocument tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!tokens.RootElement.TryGetProperty("id_token", out JsonElement idToken) || idToken.GetString() is not { } jwt)
            {
                return null;
            }

            string jwks = await http.GetStringAsync(new Uri(discovery.JwksUri), cancellationToken);
            TokenValidationResult result = await TokenHandler.ValidateTokenAsync(jwt, new TokenValidationParameters
            {
                ValidIssuer = discovery.Issuer,
                ValidAudience = provider.ClientId,
                IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
                ValidateLifetime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            });

            if (!result.IsValid)
            {
                logger.LogWarning(result.Exception, "An id_token from {Provider} failed validation.", provider.Key);
                return null;
            }

            string? nonce = result.ClaimsIdentity.FindFirst("nonce")?.Value;
            if (nonce is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(nonce), Encoding.ASCII.GetBytes(saved.Nonce)))
            {
                logger.LogWarning("An id_token from {Provider} carried the wrong nonce.", provider.Key);
                return null;
            }

            return result.ClaimsIdentity;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Single sign-on through {Provider} failed talking to the provider.", provider.Key);
            return null;
        }
    }

    private async Task<Discovery> DiscoverAsync(SsoProvider provider, CancellationToken cancellationToken)
    {
        HttpClient http = httpClients.CreateClient(HttpClientName);
        try
        {
            using JsonDocument document = JsonDocument.Parse(await http.GetStringAsync(new Uri($"{provider.Authority}/.well-known/openid-configuration"), cancellationToken));
            JsonElement root = document.RootElement;
            return new Discovery(
                root.GetProperty("issuer").GetString()!,
                root.GetProperty("authorization_endpoint").GetString()!,
                root.GetProperty("token_endpoint").GetString()!,
                root.GetProperty("jwks_uri").GetString()!);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Discovery for {Provider} failed.", provider.Key);
            throw new DomainRuleException("SSO_PROVIDER_UNREACHABLE", "The identity provider could not be reached.");
        }
    }

    private async Task<TenantInfo?> EnterTenantAsync(string? tenantCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantCode) || await tenants.GetByCodeAsync(tenantCode.Trim(), cancellationToken) is not { IsOperational: true } tenant)
        {
            return null;
        }

        tenantSetter.SetTenant(tenant);
        return tenant;
    }

    private SealedState? Unseal(string? sealedState)
    {
        if (string.IsNullOrEmpty(sealedState))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SealedState>(States.Unprotect(sealedState));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private void SetSecret(SsoProvider provider, string? secret)
    {
        if (secret is null)
        {
            return;
        }

        provider.SetClientSecret(secret.Length == 0 ? null : protector.Protect(SecretPurpose, secret, 2).Ciphertext);
    }

    private async Task<SsoProvider> FindAsync(Guid id, CancellationToken cancellationToken)
        => await db.SsoProviders.FirstOrDefaultAsync(provider => provider.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(SsoProvider), id);

    private static SsoProviderView View(SsoProvider provider)
        => new(provider.Id, provider.Key, provider.DisplayName, provider.Authority, provider.ClientId, provider.ClientSecret is not null, provider.Scopes, provider.AllowedDomains, provider.Enabled);

    private static DomainRuleException Unknown() => new("SSO_PROVIDER_UNKNOWN", "That sign-in option is not available.");

    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Only a path on this site: an open redirect would make the sign-in page a phishing tool.</summary>
    public static string SafeReturnUrl(string? returnUrl)
        => returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal) ? url : "/";

    private sealed record Discovery(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri);

    private sealed record SealedState(Guid TenantId, Guid ProviderId, string State, string Nonce, string Verifier, string ReturnUrl, string RedirectUri);
}
