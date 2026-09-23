using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainIsolationTier = Gggs.Platform.Domain.Tenants.TenantIsolationTier;
using DomainTenantStatus = Gggs.Platform.Domain.Tenants.TenantStatus;
using SharedIsolationTier = Gggs.BuildingBlocks.MultiTenancy.TenantIsolationTier;
using SharedTenantStatus = Gggs.BuildingBlocks.MultiTenancy.TenantStatus;

namespace Gggs.Platform.Infrastructure;

/// <summary>
/// The real tenant registry, replacing the Phase 0 stub that read tenants from configuration.
/// <para>
/// Every request in every service passes through this: the tenant middleware resolves the token's
/// tenant here before anything else runs. That makes it the hottest lookup in the platform and the
/// one place where a slow query would tax every screen, so results are cached.
/// </para>
/// <para>
/// The cache is short-lived rather than long, because it decides whether a suspended tenant can
/// still be served. Five minutes bounds how long a suspension takes to bite; an explicit
/// invalidation on status change makes it immediate in practice.
/// </para>
/// </summary>
public sealed class PlatformTenantRegistry(
    PlatformDbContext dbContext,
    IGggsCache cache,
    TimeProvider timeProvider,
    ILogger<PlatformTenantRegistry> logger) : ITenantRegistry
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        // Platform scope: the registry is looked up before a tenant context exists, so this cannot
        // be a tenant-scoped cache entry.
        return await cache.GetOrCreatePlatformAsync(
            CacheAreas.TenantInfo,
            tenantId.ToString("N"),
            async token => await LoadAsync(tenant => tenant.Id == tenantId, token),
            CacheDuration,
            cancellationToken);
    }

    public async Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return null;
        }

        string normalized = tenantCode.Trim().ToLowerInvariant();

        return await cache.GetOrCreatePlatformAsync(
            CacheAreas.TenantInfo,
            $"code:{normalized}",
            async token => await LoadAsync(tenant => tenant.Code == normalized, token),
            CacheDuration,
            cancellationToken);
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Clears the whole area rather than two keys: a tenant is cached by id and by code, and
        // missing one would leave a suspended tenant reachable through the login path.
        await cache.RemoveAreaAsync(CacheAreas.TenantInfo, cancellationToken);
        logger.LogInformation("Tenant cache invalidated after a change to {TenantId}.", tenantId);
    }

    private async Task<TenantInfo?> LoadAsync(
        System.Linq.Expressions.Expression<Func<Tenant, bool>> predicate,
        CancellationToken cancellationToken)
    {
        Tenant? tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (tenant is null)
        {
            return null;
        }

        IReadOnlyList<string> features = await ResolveFeaturesAsync(tenant.Id, cancellationToken);

        return new TenantInfo
        {
            Id = tenant.Id,
            Code = tenant.Code,
            Name = tenant.Name,
            Status = MapStatus(tenant.Status),
            IsolationTier = tenant.IsolationTier == DomainIsolationTier.Dedicated
                ? SharedIsolationTier.Dedicated
                : SharedIsolationTier.Pooled,
            DataRegion = tenant.DataRegion,
            CountryCode = tenant.CountryCode,
            DefaultTimeZone = tenant.DefaultTimeZone,
            DefaultCurrency = tenant.DefaultCurrency,
            DefaultLocale = tenant.DefaultLocale,
            EnabledFeatures = features,
        };
    }

    /// <summary>
    /// A tenant with no live subscription gets no features rather than all of them: failing closed
    /// is the only safe default when entitlement cannot be established.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Subscription? subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId
                    && (candidate.Status == SubscriptionStatus.Trial
                        || candidate.Status == SubscriptionStatus.Active
                        || candidate.Status == SubscriptionStatus.PastDue),
                cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning("Tenant {TenantId} has no live subscription; no features are enabled.", tenantId);
            return [];
        }

        Plan? plan = await dbContext.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == subscription.PlanId, cancellationToken);

        if (plan is null)
        {
            logger.LogError(
                "Subscription {SubscriptionId} references plan {PlanId}, which does not exist.",
                subscription.Id,
                subscription.PlanId);
            return [];
        }

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return [.. subscription.ResolveFeatures(plan, today)];
    }

    private static SharedTenantStatus MapStatus(DomainTenantStatus status) => status switch
    {
        DomainTenantStatus.Provisioning => SharedTenantStatus.Provisioning,
        DomainTenantStatus.Active => SharedTenantStatus.Active,
        DomainTenantStatus.Suspended => SharedTenantStatus.Suspended,
        DomainTenantStatus.Offboarding => SharedTenantStatus.Offboarding,
        DomainTenantStatus.Archived => SharedTenantStatus.Archived,
        DomainTenantStatus.Purged => SharedTenantStatus.Purged,

        // A status the shared contract does not know about must not be treated as operational.
        _ => SharedTenantStatus.Suspended,
    };
}
