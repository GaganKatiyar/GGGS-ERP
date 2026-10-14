using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.MasterData.Application;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.MasterData.Domain;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.MasterData.Infrastructure;

/// <summary>
/// Gives the current tenant every catalog value it does not have yet. Idempotent: run it at
/// provisioning, after a release that adds values, or by hand - existing values, including a
/// tenant's own edits, are never touched.
/// </summary>
public sealed class MasterDataSeeder(AppDbContext dbContext, IGggsCache cache, ILogger<MasterDataSeeder> logger)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        var existing = (await dbContext.LookupValues
                .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
                .Select(value => new { value.ListCode, value.Code })
                .ToListAsync(cancellationToken))
            .Select(value => (value.ListCode, value.Code))
            .ToHashSet();

        int added = 0;

        foreach ((string listCode, IReadOnlyList<LookupSeed> seeds) in LookupCatalog.Seeds)
        {
            for (int index = 0; index < seeds.Count; index++)
            {
                LookupSeed seed = seeds[index];
                if (existing.Contains((listCode, LookupValue.NormalizeCode(seed.Code))))
                {
                    continue;
                }

                dbContext.LookupValues.Add(LookupValue.Create(
                    listCode, seed.Code, seed.Label, seed.Parent, sortOrder: (index + 1) * 10, isSystem: true));
                added++;
            }
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await cache.RemoveAreaAsync(CacheAreas.Lookups, cancellationToken);
        }

        logger.LogInformation("Master data seeded: {Added} value(s) added.", added);
        return added;
    }
}

/// <summary>Cached, tenant-scoped validation other modules call through <see cref="IMasterDataLookup"/>.</summary>
internal sealed class MasterDataLookup(AppDbContext dbContext, IGggsCache cache) : IMasterDataLookup
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public async Task<bool> IsValidAsync(string listCode, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listCode) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        IReadOnlyDictionary<string, string> active = await ActiveValuesAsync(listCode, cancellationToken);
        return active.ContainsKey(LookupValue.NormalizeCode(code));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string listCode,
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);

        IReadOnlyDictionary<string, string> active = await ActiveValuesAsync(listCode, cancellationToken);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string code in codes.Where(code => !string.IsNullOrWhiteSpace(code)))
        {
            if (active.TryGetValue(LookupValue.NormalizeCode(code), out string? label))
            {
                labels[code] = label;
            }
        }

        return labels;
    }

    private async Task<IReadOnlyDictionary<string, string>> ActiveValuesAsync(string listCode, CancellationToken cancellationToken)
    {
        string normalizedList = listCode.Trim().ToLowerInvariant();

        LookupLabel[] values = await cache.GetOrCreateAsync(
            CacheAreas.Lookups,
            normalizedList,
            async token => await dbContext.LookupValues
                .AsNoTracking()
                .Where(value => value.ListCode == normalizedList && value.IsActive)
                .Select(value => new LookupLabel(value.Code, value.Label))
                .ToArrayAsync(token),
            CacheDuration,
            cancellationToken);

        return values.ToDictionary(value => value.Code, value => value.Label, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Plain data for the cache: it must round-trip through JSON.</summary>
    internal sealed record LookupLabel(string Code, string Label);
}

/// <summary>A new tenant starts with the catalog's lookup values.</summary>
internal sealed class SeedMasterDataOnTenantProvisioned(MasterDataSeeder seeder)
    : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public async Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => await seeder.SeedCurrentTenantAsync(cancellationToken);
}
