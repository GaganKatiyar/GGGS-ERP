using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.MultiTenancy;

namespace Gggs.BuildingBlocks.Files;

/// <summary>
/// Builds and validates storage keys so a tenant's files live under a prefix only that tenant can
/// address.
/// <para>
/// Both adapters route through here, because the two ways to break file isolation are a traversal
/// sequence in a caller-supplied name and a key from another tenant being passed back in. Each gets
/// rejected once, in one place.
/// </para>
/// </summary>
public static partial class TenantStorageKey
{
    /// <summary>Container or folder holding one tenant's files, for example <c>t-9f3c...</c>.</summary>
    public static string ContainerFor(Guid tenantId) => $"t-{tenantId:N}";

    /// <summary>Prefixes a relative key with the current tenant's container.</summary>
    public static string Build(ITenantContext tenantContext, string relativeKey)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        string safeKey = Sanitize(relativeKey);
        return $"{ContainerFor(tenantContext.TenantId)}/{safeKey}";
    }

    /// <summary>
    /// Splits a full key and refuses it if it does not belong to the current tenant. Callers get a
    /// mismatch exception, which the API surfaces as 404 and logs as a security event.
    /// </summary>
    public static (string Container, string RelativeKey) Resolve(ITenantContext tenantContext, string storageKey)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        int separator = storageKey.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException("Storage key is missing its tenant prefix.", nameof(storageKey));
        }

        string container = storageKey[..separator];
        string relativeKey = storageKey[(separator + 1)..];
        string expected = ContainerFor(tenantContext.TenantId);

        if (!string.Equals(container, expected, StringComparison.Ordinal))
        {
            throw new TenantMismatchException(tenantContext.TenantId, Guid.Empty, "StoredFile");
        }

        // Re-validate after splitting: a key that passed on the way in can still be tampered with
        // between the database and this call.
        return (container, Sanitize(relativeKey));
    }

    /// <summary>
    /// Rejects traversal, absolute paths, alternate data streams and control characters, and keeps
    /// keys to a portable character set that behaves the same on Blob Storage and on NTFS.
    /// </summary>
    public static string Sanitize(string relativeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeKey);

        string normalized = relativeKey.Replace('\\', '/').Trim('/');

        if (normalized.Length == 0 || normalized.Length > 900)
        {
            throw new ArgumentException("Storage key length is out of range.", nameof(relativeKey));
        }

        if (normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Storage key contains an unsafe path segment.", nameof(relativeKey));
        }

        if (!SafeKeyPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Storage key contains unsupported characters.", nameof(relativeKey));
        }

        return normalized;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._\-/]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKeyPattern();
}
