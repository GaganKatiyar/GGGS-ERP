using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Platform.Domain.Console;
using Gggs.Modules.Platform.Infrastructure;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Gggs.Modules.Platform.Application.Usage;

/// <summary>Every hour, measures each active organisation's figures for the platform console.</summary>
[PlatformJob("Platform usage: fans out one measurement per active tenant for the platform console.")]
internal sealed class PlatformUsageSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "platform-usage";

    public Task RunAsync() => fanOut.FanOutAsync<PlatformUsageJob>((job, tenantId) => job.RunAsync(tenantId));
}

/// <summary>
/// Runs inside one tenant: each module counts its own rows (row-level security in force), and only
/// the counts are written to the platform-owned <see cref="TenantUsage"/> row.
/// </summary>
internal sealed class PlatformUsageJob(IEnumerable<ITenantUsageProbe> probes, PlatformUsageRecorder recorder, ITenantContext tenantContext, ITenantClock clock, TimeProvider timeProvider)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime todayStartUtc = await clock.ToUtcAsync(await clock.TodayAsync(CancellationToken.None), TimeOnly.MinValue, CancellationToken.None);

        var reading = new TenantUsageReading();
        foreach (ITenantUsageProbe probe in probes)
        {
            await probe.ReadAsync(reading, todayStartUtc, CancellationToken.None);
        }

        await recorder.SaveAsync(tenantContext.TenantId, reading, now, CancellationToken.None);
    }
}

/// <summary>Writes one organisation's reading. Separate so tests can call it without a job server.</summary>
public sealed class PlatformUsageRecorder(AppDbContext dbContext)
{
    public async Task SaveAsync(Guid tenantId, TenantUsageReading reading, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);
        TenantUsage? usage = await dbContext.TenantUsages.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        if (usage is null)
        {
            usage = TenantUsage.For(tenantId);
            dbContext.TenantUsages.Add(usage);
        }

        usage.Update(
            now,
            reading.Employees ?? 0,
            reading.Users ?? 0,
            reading.SignedInToday ?? 0,
            reading.LastSignIn,
            reading.EmailsSentToday ?? 0,
            reading.EmailsFailedToday ?? 0,
            reading.EmailsLoggedToday ?? 0,
            reading.EmailChannel,
            reading.SignIn);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class PlatformUsageJobRegistration(IRecurringJobManager recurringJobs, IBackgroundJobClient backgroundJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<PlatformUsageSweep>(PlatformUsageSweep.RecurringJobName, sweep => sweep.RunAsync(), "10 * * * *");

        // Once at start too, so a fresh install or a new organisation has figures without waiting an hour.
        backgroundJobs.Enqueue<PlatformUsageSweep>(sweep => sweep.RunAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
