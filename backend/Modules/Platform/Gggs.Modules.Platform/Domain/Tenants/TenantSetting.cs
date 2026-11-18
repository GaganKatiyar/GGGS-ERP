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

