using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Reporting.Application.Headcount;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gggs.Reporting.Worker;

/// <summary>
/// Builds today's <see cref="Gggs.Reporting.Domain.Headcount.HeadcountDailySnapshot"/> rows for
/// every tenant, once a day - architecture.md §9's "recurring jobs fan out per tenant" rule
/// (layer 10), here as a simple timer loop rather than Hangfire, since no job scheduler building
/// block exists anywhere in this codebase yet (see this project's own remarks on why one wasn't
/// introduced just for this slice).
/// <para>
/// Runs once at startup too, not only on the daily tick, so a freshly started dev/test environment
/// has at least one snapshot to query without waiting up to 24 hours for the first tick.
/// </para>
/// </summary>
public sealed class HeadcountSnapshotJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<HeadcountSnapshotJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateOnly lastRunDate = default;

        while (!stoppingToken.IsCancellationRequested)
        {
            DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            if (today != lastRunDate)
            {
                await RunForEveryTenantAsync(today, stoppingToken);
                lastRunDate = today;
            }

            try
            {
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunForEveryTenantAsync(DateOnly snapshotDate, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope lookupScope = scopeFactory.CreateAsyncScope();

        ITenantContextSetter lookupTenantSetter = lookupScope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        using IDisposable lookupPlatformScope = lookupTenantSetter.BeginPlatformScope("headcount-snapshot-job");

        IEmployeeRosterFactRepository lookupRoster = lookupScope.ServiceProvider.GetRequiredService<IEmployeeRosterFactRepository>();
        IReadOnlyList<Guid> tenantIds = await lookupRoster.ListTenantIdsAsync(cancellationToken);

        logger.LogInformation("Building headcount snapshots for {SnapshotDate} across {TenantCount} tenant(s).", snapshotDate, tenantIds.Count);

        foreach (Guid tenantId in tenantIds)
        {
            try
            {
                await using AsyncServiceScope tenantScope = scopeFactory.CreateAsyncScope();

                ITenantContextSetter tenantSetter = tenantScope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
                using IDisposable platformScope = tenantSetter.BeginPlatformScope("headcount-snapshot-job");

                HeadcountSnapshotBuilder builder = tenantScope.ServiceProvider.GetRequiredService<HeadcountSnapshotBuilder>();
                await builder.BuildForTenantAsync(tenantId, snapshotDate, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One tenant's roster data must never block the rest - logged and retried on the
                // next tick rather than crashing the job for every other tenant.
                logger.LogError(exception, "Headcount snapshot build failed for tenant {TenantId}.", tenantId);
            }
        }
    }
}
