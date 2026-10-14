using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Infrastructure;

/// <summary>
/// Brings <c>platform.Plan</c> in line with <see cref="PlanCatalog"/>: missing plans are created and
/// each catalog plan's feature list is made to match. Plans that are no longer in the catalog are
/// left alone - retiring one is a commercial decision, made through the API, not by deleting a line.
/// </summary>
public sealed class PlanCatalogSynchronizer(
    AppDbContext dbContext,
    ITenantContextSetter tenantSetter,
    ILogger<PlanCatalogSynchronizer> logger)
{
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable platformScope = tenantSetter.BeginPlatformScope("plan-catalog-sync");

        Dictionary<string, Plan> existing = await dbContext.Plans
            .ToDictionaryAsync(plan => plan.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        int created = 0;
        int updated = 0;

        foreach (PlanDefinition definition in PlanCatalog.Definitions)
        {
            if (!existing.TryGetValue(definition.Code, out Plan? plan))
            {
                dbContext.Plans.Add(Plan.Create(
                    definition.Code,
                    definition.Name,
                    definition.Edition,
                    definition.MaxEmployees,
                    isPubliclyAvailable: true,
                    definition.Features));
                created++;
                continue;
            }

            var wanted = new HashSet<string>(definition.Features, StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (string feature in plan.FeatureCodes.Where(code => !wanted.Contains(code)).ToList())
            {
                plan.RemoveFeature(feature);
                changed = true;
            }

            foreach (string feature in wanted.Where(code => !plan.FeatureCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
            {
                plan.AddFeature(feature);
                changed = true;
            }

            updated += changed ? 1 : 0;
        }

        if (created + updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Plan catalog synchronised: {Created} created, {Updated} updated.", created, updated);
    }
}

/// <summary>Runs the plan synchronisation once at worker start-up.</summary>
internal sealed class PlanCatalogSeeder(IServiceScopeFactory scopeFactory, ILogger<PlanCatalogSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PlanCatalogSynchronizer>().SyncAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged, not fatal: existing tenants keep working from the plans already stored, and a
            // crash loop on a customer's server is harder to diagnose than this message.
            logger.LogError(exception, "The plan catalog could not be synchronised.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
