using Gggs.BuildingBlocks.Domain.Exceptions;

namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// Tenant-owned data was touched without a resolved tenant. Always a bug, never a user error:
/// it means a code path escaped the middleware, message filter or job filter.
/// </summary>
public sealed class TenantNotResolvedException()
    : GggsException("TENANT_NOT_RESOLVED", "No tenant has been resolved for the current operation.");

/// <summary>
/// An entity belonging to another tenant reached this operation. Treated as a security incident:
/// the request fails, a security event is written and an alert is raised. Clients see 404.
/// </summary>
public sealed class TenantMismatchException(Guid expectedTenantId, Guid actualTenantId, string entityType)
    : GggsException(
        "TENANT_MISMATCH",
        $"A {entityType} belonging to another tenant was accessed in the current tenant scope.")
{
    public Guid ExpectedTenantId { get; } = expectedTenantId;

    public Guid ActualTenantId { get; } = actualTenantId;

    public string EntityType { get; } = entityType;
}

/// <summary>The tenant exists but cannot serve requests: provisioning, suspended or being offboarded.</summary>
public sealed class TenantUnavailableException(Guid tenantId, TenantStatus status)
    : GggsException(
        status is TenantStatus.Suspended ? "TENANT_SUSPENDED" : "TENANT_UNAVAILABLE",
        $"Tenant is not available for use (status: {status}).")
{
    public Guid TenantId { get; } = tenantId;

    public TenantStatus Status { get; } = status;
}

/// <summary>The module is not part of the tenant's plan, or has been turned off for them.</summary>
public sealed class FeatureNotEnabledException(string featureCode)
    : GggsException("FEATURE_NOT_ENABLED", $"Feature '{featureCode}' is not enabled for this tenant.")
{
    public string FeatureCode { get; } = featureCode;
}
