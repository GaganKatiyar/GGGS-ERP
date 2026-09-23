namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// The tenant the current operation belongs to. Injected into DbContexts, repositories, cache keys,
/// message publishers and job schedulers, so tenant scoping is never something a developer has to
/// remember to apply.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant. Throws when nothing has been resolved, which is deliberate:
    /// tenant-owned data must never be read or written without a tenant.</summary>
    Guid TenantId { get; }

    /// <summary>True once a tenant has been resolved for this scope.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// True inside an explicit platform scope (migrations, tenant provisioning, cross-tenant usage
    /// metrics). Code paths that set this run under a separate database principal.
    /// </summary>
    bool IsPlatformScope { get; }

    /// <summary>Tenant metadata (status, region, time zone, enabled features) when it has been loaded.</summary>
    TenantInfo? Tenant { get; }
}

/// <summary>Mutable side of <see cref="ITenantContext"/>, available only to infrastructure code.</summary>
public interface ITenantContextSetter
{
    void SetTenant(TenantInfo tenant);

    void SetTenant(Guid tenantId);

    /// <summary>
    /// Enters a platform-wide scope for the duration of the returned disposable. Every use is a
    /// deliberate exception to tenant isolation and is logged and audited.
    /// </summary>
    IDisposable BeginPlatformScope(string reason);
}

/// <summary>
/// Scoped implementation. One instance per request, message or job, so an ambient static is not
/// needed and parallel work on different tenants cannot bleed into each other.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId
        ?? throw new TenantNotResolvedException();

    public bool IsResolved => _tenantId.HasValue;

    public bool IsPlatformScope { get; private set; }

    public TenantInfo? Tenant { get; private set; }

    /// <summary>Why the current platform scope was entered; surfaced in logs and audit records.</summary>
    public string? PlatformScopeReason { get; private set; }

    public void SetTenant(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        Tenant = tenant;
        _tenantId = tenant.Id;
    }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        _tenantId = tenantId;
    }

    public IDisposable BeginPlatformScope(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsPlatformScope = true;
        PlatformScopeReason = reason;
        return new PlatformScope(this);
    }

    private sealed class PlatformScope(TenantContext owner) : IDisposable
    {
        public void Dispose()
        {
            owner.IsPlatformScope = false;
            owner.PlatformScopeReason = null;
        }
    }
}
