using System.Security.Cryptography;
using System.Text;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Identity.Domain.ApiKeys;

/// <summary>
/// A key an outside system uses to call the API. It acts as its own machine account (a user of
/// type <c>ApiClient</c>) holding the roles chosen for it, so it can do exactly what those roles
/// allow - the same permissions and data scopes as a person - and nothing else. Only the hash of
/// its secret is kept; the full key is shown once, when it is made.
/// </summary>
public sealed class ApiKey : TenantAggregateRoot
{
    private ApiKey()
    {
        Name = null!;
        Prefix = null!;
        SecretHash = null!;
    }

    public string Name { get; private set; }

    /// <summary>The key's public part, shown in lists and used to find it; never secret.</summary>
    public string Prefix { get; private set; }

    /// <summary>SHA-256 of the secret part, hex.</summary>
    public string SecretHash { get; private set; }

    /// <summary>The machine account the key signs in as.</summary>
    public Guid ServiceUserId { get; private set; }

    /// <summary>The last day the key works; null for a key that does not expire.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public static ApiKey Create(string name, string prefix, string secretHash, Guid serviceUserId, DateOnly? expiresOn, Guid? createdByUserId) => new()
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        Prefix = Guard.MaxLength(Guard.NotNullOrWhiteSpace(prefix), 16),
        SecretHash = Guard.MaxLength(Guard.NotNullOrWhiteSpace(secretHash), 64),
        ServiceUserId = Guard.NotEmpty(serviceUserId),
        ExpiresOn = expiresOn,
        CreatedByUserId = createdByUserId,
    };

    public bool IsUsableOn(DateTime now) => RevokedAt is null && (ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(now));

    public void Revoke(DateTime now)
    {
        Guard.Against(RevokedAt is not null, "API_KEY_REVOKED", "This key is already revoked.");
        RevokedAt = now;
    }

    /// <summary>Records use, at most once a minute so a busy integration does not write on every call. True when it changed.</summary>
    public bool Touch(DateTime now)
    {
        if (LastUsedAt is { } last && now - last < TimeSpan.FromMinutes(1))
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    /// <summary>Whether the given secret is this key's, compared in constant time.</summary>
    public bool Matches(string secret)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ApiKeyFormat.Hash(secret)), Encoding.ASCII.GetBytes(SecretHash));
}

/// <summary>A parsed key: which organisation it belongs to, its prefix and its secret.</summary>
public sealed record ApiKeyParts(string TenantCode, string Prefix, string Secret);

/// <summary>
/// The key's shape: <c>pk_{tenantCode}_{prefix}_{secret}</c>. The organisation code says where to
/// look before any tenant is known; the prefix finds the key; the secret proves it.
/// </summary>
public static class ApiKeyFormat
{
    public const string Marker = "pk";

    private const string PrefixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    public static (string Key, string Prefix, string Secret) Generate(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        string prefix = new([.. Enumerable.Range(0, 8).Select(_ => PrefixAlphabet[RandomNumberGenerator.GetInt32(PrefixAlphabet.Length)])]);
        string secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return ($"{Marker}_{tenantCode.Trim().ToLowerInvariant()}_{prefix}_{secret}", prefix, secret);
    }

    /// <summary>The key's parts, or null when it is not a Nuts and Bolts key at all.</summary>
    public static ApiKeyParts? Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string[] parts = key.Trim().Split('_');
        if (parts.Length < 4 || parts[0] != Marker)
        {
            return null;
        }

        string secret = parts[^1];
        string prefix = parts[^2];
        string tenantCode = string.Join('_', parts[1..^2]);
        return tenantCode.Length == 0 || prefix.Length != 8 || secret.Length != 64 ? null : new ApiKeyParts(tenantCode, prefix, secret);
    }

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
