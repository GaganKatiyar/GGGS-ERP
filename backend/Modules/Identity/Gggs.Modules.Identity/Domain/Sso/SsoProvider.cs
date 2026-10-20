using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Identity.Domain.Sso;

/// <summary>
/// An organisation's own identity provider (Entra ID, Okta, Google Workspace, Keycloak - anything that
/// speaks OpenID Connect), offered on the sign-in page as "Sign in with ...".
/// <para>
/// Federation links to accounts that already exist - by verified email address, restricted to the
/// domains listed - and never creates one: who gets access, and with which roles, stays a decision
/// made here, not in the provider's directory.
/// </para>
/// </summary>
public sealed class SsoProvider : TenantAggregateRoot
{
    private SsoProvider()
    {
        Key = null!;
        DisplayName = null!;
        Authority = null!;
        ClientId = null!;
        Scopes = null!;
    }

    /// <summary>Short and URL-safe; part of the sign-in link and of each linked login.</summary>
    public string Key { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>The issuer; discovery is read from <c>{Authority}/.well-known/openid-configuration</c>.</summary>
    public string Authority { get; private set; }

    public string ClientId { get; private set; }

    /// <summary>Encrypted. Absent for a public client that relies on PKCE alone.</summary>
    public string? ClientSecret { get; private set; }

    public string Scopes { get; private set; }

    /// <summary>Comma-separated email domains whose accounts may sign in this way; empty allows any.</summary>
    public string? AllowedDomains { get; private set; }

    public bool Enabled { get; private set; }

    public static SsoProvider Create(string key, string displayName, string authority, string clientId, string? scopes, string? allowedDomains, bool enabled)
    {
        var provider = new SsoProvider { Key = NormalizeKey(key) };
        provider.Update(displayName, authority, clientId, scopes, allowedDomains, enabled);
        return provider;
    }

    public void Update(string displayName, string authority, string clientId, string? scopes, string? allowedDomains, bool enabled)
    {
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName).Trim(), 100);
        string issuer = Guard.NotNullOrWhiteSpace(authority).Trim().TrimEnd('/');
        Guard.Against(
            !Uri.TryCreate(issuer, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback),
            "SSO_AUTHORITY_INVALID",
            "The authority must be an https address.");
        Authority = Guard.MaxLength(issuer, 300);
        ClientId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(clientId).Trim(), 200);
        string requested = string.IsNullOrWhiteSpace(scopes) ? "openid profile email" : scopes.Trim();
        Scopes = Guard.MaxLength(requested.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid") ? requested : $"openid {requested}", 300);
        AllowedDomains = string.IsNullOrWhiteSpace(allowedDomains)
            ? null
            : Guard.MaxLength(string.Join(',', allowedDomains.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(domain => domain.Trim().TrimStart('@').ToLowerInvariant())), 500);
        Enabled = enabled;
    }

    public void SetClientSecret(string? ciphertext) => ClientSecret = ciphertext;

    public bool AllowsEmail(string email)
    {
        if (AllowedDomains is null)
        {
            return true;
        }

        string domain = email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
        return AllowedDomains.Split(',').Contains(domain);
    }

    /// <summary>The provider name recorded on linked logins.</summary>
    public string LoginProvider => $"oidc:{Key}";

    private static string NormalizeKey(string key)
    {
        string normalized = Guard.NotNullOrWhiteSpace(key).Trim().ToLowerInvariant();
        Guard.Against(
            normalized.Length > 40 || !normalized.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'),
            "SSO_KEY_INVALID",
            "The key may use lower-case letters, digits and hyphens, up to 40 characters.");
        return normalized;
    }
}
