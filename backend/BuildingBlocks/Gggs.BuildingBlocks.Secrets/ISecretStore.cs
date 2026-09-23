namespace Gggs.BuildingBlocks.Secrets;

/// <summary>
/// Resolves secrets by name: tenant database connection strings, per-tenant provider credentials
/// (SMTP, SMS, bank SFTP), signing keys and encryption keys.
/// <para>
/// Nothing in the platform stores a secret value in configuration or in the database. Records hold
/// a secret <em>name</em>, and this port turns that into a value at the moment it is needed, which
/// keeps credentials out of backups, logs and screenshots of admin screens.
/// </para>
/// </summary>
public interface ISecretStore
{
    /// <summary>Throws when the secret does not exist; the caller cannot continue without it.</summary>
    Task<string> GetAsync(string secretName, CancellationToken cancellationToken = default);

    Task<string?> TryGetAsync(string secretName, CancellationToken cancellationToken = default);

    /// <summary>Creates or rotates a secret. Used by tenant provisioning and rotation runbooks.</summary>
    Task SetAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);

    Task DeleteAsync(string secretName, CancellationToken cancellationToken = default);
}

/// <summary>The named secret was not found in the configured store.</summary>
public sealed class SecretNotFoundException(string secretName)
    : InvalidOperationException($"Secret '{secretName}' was not found in the configured secret store.")
{
    public string SecretName { get; } = secretName;
}

/// <summary>Naming so a secret can be traced to its owner without opening it.</summary>
public static class SecretNames
{
    /// <summary>A tenant's database connection for one service, for example <c>tenant-9f3c-corehr-db</c>.</summary>
    public static string TenantDatabase(Guid tenantId, string serviceName)
        => $"tenant-{tenantId:N}-{serviceName.ToLowerInvariant()}-db";

    /// <summary>Credentials for a tenant's integration connection.</summary>
    public static string TenantConnector(Guid tenantId, string connectorCode)
        => $"tenant-{tenantId:N}-connector-{connectorCode.ToLowerInvariant()}";

    /// <summary>The key encrypting a tenant's field-level data keys; destroying it crypto-shreds their PII.</summary>
    public static string TenantDataKey(Guid tenantId) => $"tenant-{tenantId:N}-data-key";
}
