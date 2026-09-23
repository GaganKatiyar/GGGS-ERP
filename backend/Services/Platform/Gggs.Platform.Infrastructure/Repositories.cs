using Gggs.Platform.Application.Provisioning;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Platform.Infrastructure;

/// <summary>
/// Tenant persistence.
/// <para>
/// Every read here runs in platform scope by definition: this is the registry that resolves a
/// tenant, so it cannot be filtered by one.
/// </para>
/// </summary>
public sealed class TenantRepository(PlatformDbContext dbContext) : ITenantRepository
{
    public async Task<Tenant?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);

    /// <summary>
    /// Checks for a code in use, including tenants that have been purged: a purged code is never
    /// reissued, or old links and cached references would resolve to a different customer.
    /// </summary>
    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToLowerInvariant();
        return await dbContext.Tenants.AnyAsync(tenant => tenant.Code == normalized, cancellationToken);
    }

    public void Add(Tenant tenant) => dbContext.Tenants.Add(tenant);

    public void AddSubscription(Subscription subscription) => dbContext.Subscriptions.Add(subscription);
}

public sealed class PlanRepository(PlatformDbContext dbContext) : IPlanRepository
{
    public async Task<Plan?> GetByCodeAsync(string planCode, CancellationToken cancellationToken = default)
    {
        string normalized = planCode.Trim().ToUpperInvariant();
        return await dbContext.Plans.FirstOrDefaultAsync(plan => plan.Code == normalized, cancellationToken);
    }
}
