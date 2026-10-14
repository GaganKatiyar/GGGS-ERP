using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Platform.Domain.Tenants;

/// <summary>
/// One configuration value a tenant admin controls, such as the working week or a notification
/// preference.
/// <para>
/// Tenant-owned, unlike the rest of this service: these are the customer's settings, so they are
/// filtered and RLS-protected like any other tenant data. Commercial entitlements deliberately
/// live on the subscription instead, where a tenant admin cannot reach them.
/// </para>
/// </summary>
public sealed class TenantSetting : TenantAggregateRoot
{
    private TenantSetting()
    {
        Module = null!;
        Key = null!;
        ValueJson = null!;
    }

    /// <summary>Owning module, e.g. <c>leave</c>, so settings screens can load only what they need.</summary>
    public string Module { get; private set; }

    public string Key { get; private set; }

    /// <summary>
    /// JSON, so a setting can be a flag, a number or a structure without a migration each time a
    /// module adds one.
    /// </summary>
    public string ValueJson { get; private set; }

    public static TenantSetting Create(string module, string key, string valueJson) => new()
    {
        Module = Guard.MaxLength(Guard.NotNullOrWhiteSpace(module).Trim().ToLowerInvariant(), 50),
        Key = Guard.MaxLength(Guard.NotNullOrWhiteSpace(key).Trim(), 100),
        ValueJson = Guard.NotNullOrWhiteSpace(valueJson),
    };

    public void SetValue(string valueJson)
        => ValueJson = Guard.NotNullOrWhiteSpace(valueJson);
}

/// <summary>
/// Where one tenant's data lives for one service: the routing table behind ADR-01.
/// <para>
/// Pooled tenants share a database and have no row here. A tenant promoted to the dedicated tier
/// gets one per service, and the connection resolver reads it to find their database. The
/// connection string itself is never stored - only the name of the secret that holds it.
/// </para>
/// </summary>
public sealed class TenantDatabase : AggregateRoot, IPlatformOwned, IAuditable
{
    private TenantDatabase()
    {
        ServiceName = null!;
        ConnectionSecretName = null!;
        ShardName = null!;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Service that owns the database, e.g. <c>corehr</c>.</summary>
    public string ServiceName { get; private set; }

    /// <summary>Key Vault secret name, resolved at runtime through <c>ISecretStore</c>.</summary>
    public string ConnectionSecretName { get; private set; }

    /// <summary>Server or elastic pool holding it, for capacity planning and incident triage.</summary>
    public string ShardName { get; private set; }

    /// <summary>Last migration applied, so a fan-out migration run knows what still needs work.</summary>
    public string? SchemaVersion { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public static TenantDatabase Create(Guid tenantId, string serviceName, string connectionSecretName, string shardName) => new()
    {
        Id = SequentialGuid.Create(),
        TenantId = Guard.NotEmpty(tenantId),
        ServiceName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(serviceName).Trim().ToLowerInvariant(), 50),
        ConnectionSecretName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(connectionSecretName).Trim(), 200),
        ShardName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(shardName).Trim(), 100),
    };

    public void RecordSchemaVersion(string schemaVersion)
        => SchemaVersion = Guard.MaxLength(Guard.NotNullOrWhiteSpace(schemaVersion), 50);
}
