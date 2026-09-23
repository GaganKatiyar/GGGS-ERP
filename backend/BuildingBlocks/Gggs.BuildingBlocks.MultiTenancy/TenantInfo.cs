using Gggs.BuildingBlocks.Domain.Abstractions;

namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// The slice of tenant configuration every service needs on each request. Served from the Platform
/// service and cached; it deliberately carries no HR data.
/// </summary>
public sealed record TenantInfo
{
    public required Guid Id { get; init; }

    /// <summary>Short code used for login discovery and subdomains, for example <c>acme</c>.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required TenantStatus Status { get; init; }

    public required TenantIsolationTier IsolationTier { get; init; }

    /// <summary>Azure region holding this tenant's data; fixed at provisioning for residency.</summary>
    public required string DataRegion { get; init; }

    public required string CountryCode { get; init; }

    /// <summary>IANA time zone, for example <c>Asia/Kolkata</c>.</summary>
    public required string DefaultTimeZone { get; init; }

    public required string DefaultCurrency { get; init; }

    public required string DefaultLocale { get; init; }

    /// <summary>
    /// Feature codes enabled by the subscription plus per-tenant overrides.
    /// <para>
    /// A plain list rather than a set, because this record is cached and therefore serialized.
    /// <c>IReadOnlySet&lt;string&gt;</c> cannot be deserialized at all, and a <c>HashSet</c> loses
    /// its comparer on the way back - so a case-insensitive lookup would pass in memory and start
    /// failing the moment another instance read the same entry from Redis.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> EnabledFeatures { get; init; } = [];

    public bool IsOperational => Status is TenantStatus.Active;

    /// <summary>
    /// Case-insensitive by explicit comparison rather than by the collection's comparer, for the
    /// reason above.
    /// </summary>
    public bool HasFeature(string featureCode)
        => EnabledFeatures.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Looks up tenant metadata, cached close to each service (memory plus Redis).</summary>
public interface ITenantRegistry
{
    Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached copy after a tenant, plan or feature change.</summary>
    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
