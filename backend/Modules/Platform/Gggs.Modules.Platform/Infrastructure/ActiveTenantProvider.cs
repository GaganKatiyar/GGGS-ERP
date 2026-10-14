using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Infrastructure;

/// <summary>
/// The tenants a per-tenant job fan-out covers: active ones only, so a suspended or offboarding
/// tenant's data is left alone by scheduled work.
/// </summary>
internal sealed class ActiveTenantProvider(AppDbContext dbContext, ITenantContextSetter tenantSetter) : IActiveTenantProvider
{
    public async Task<IReadOnlyCollection<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        // The registry is platform-owned and spans tenants by nature.
        using IDisposable platformScope = tenantSetter.BeginPlatformScope("job-fan-out");

        return await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active)
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
    }
}
