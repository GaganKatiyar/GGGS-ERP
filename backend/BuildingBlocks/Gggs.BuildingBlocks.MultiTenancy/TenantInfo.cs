namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>Lifecycle states from the tenant provisioning saga (§3.4).</summary>
public enum TenantStatus
{
    Provisioning = 1,
    Active = 2,
    Suspended = 3,
    Offboarding = 4,
    Archived = 5,
    Purged = 6,
}

/// <summary>Pooled (shared database) or dedicated database per ADR-01.</summary>
public enum TenantIsolationTier
{
    Pooled = 1,
    Dedicated = 2,
}

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

    /// <summary>Feature codes enabled by the subscription plus per-tenant overrides.</summary>
    public IReadOnlySet<string> EnabledFeatures { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsOperational => Status is TenantStatus.Active;

    public bool HasFeature(string featureCode) => EnabledFeatures.Contains(featureCode);
}

/// <summary>Looks up tenant metadata, cached close to each service (memory plus Redis).</summary>
public interface ITenantRegistry
{
    Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached copy after a tenant, plan or feature change.</summary>
    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
