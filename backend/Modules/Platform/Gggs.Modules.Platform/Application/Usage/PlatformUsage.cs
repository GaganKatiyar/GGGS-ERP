using System.Text.Json;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Platform.Infrastructure;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Platform.Application.Usage;

/*
 * The HR application's side of the handshake with the platform console (NaBPlatform). The platform
 * never connects to this database: it asks with ProvisioningRequest rows, and this worker reports back
 * into NaBPlatform's `hr` schema. See docs/nabplatform-contract.md.
 */

/// <summary>Every hour, measures each active organisation for the platform console.</summary>
[PlatformJob("Platform usage: fans out one measurement per active tenant for the platform console.")]
internal sealed class PlatformUsageSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "platform-usage";

    public Task RunAsync() => fanOut.FanOutAsync<PlatformUsageJob>((job, tenantId) => job.RunAsync(tenantId));
}

/// <summary>
/// Runs inside one tenant: each module counts its own rows (row-level security in force), and only the
/// counts - plus the administrators' names, which the console shows - are written to NaBPlatform.
/// </summary>
internal sealed class PlatformUsageJob(
    IEnumerable<ITenantUsageProbe> probes,
    IOrganisationAdministrators administrators,
    PlatformUsageRecorder recorder,
    ITenantContext tenantContext,
    ITenantClock clock,
    TimeProvider timeProvider)
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
        await recorder.SaveAdministratorsAsync(tenantContext.TenantId, await administrators.ListAsync(CancellationToken.None), now, CancellationToken.None);
    }
}

/// <summary>Writes one organisation's figures and administrators into NaBPlatform.</summary>
public sealed class PlatformUsageRecorder(PlatformCatalogDbContext catalog)
{
    public async Task SaveAsync(Guid tenantId, TenantUsageReading reading, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);
        TenantUsageRow? usage = await catalog.TenantUsages.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        if (usage is null)
        {
            usage = new TenantUsageRow { Id = Guid.CreateVersion7(), TenantId = tenantId };
            catalog.TenantUsages.Add(usage);
        }

        usage.MeasuredDate = now;
        usage.Employees = Math.Max(0, reading.Employees ?? 0);
        usage.Users = Math.Max(0, reading.Users ?? 0);
        usage.SignedInToday = Math.Max(0, reading.SignedInToday ?? 0);
        usage.LastSignIn = reading.LastSignIn;
        usage.EmailsSentToday = Math.Max(0, reading.EmailsSentToday ?? 0);
        usage.EmailsFailedToday = Math.Max(0, reading.EmailsFailedToday ?? 0);
        usage.EmailsLoggedToday = Math.Max(0, reading.EmailsLoggedToday ?? 0);
        usage.EmailChannel = Trim(reading.EmailChannel, 200);
        usage.SignIn = Trim(reading.SignIn, 200);
        await catalog.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAdministratorsAsync(Guid tenantId, IReadOnlyList<OrganisationAdministratorInfo> admins, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admins);
        catalog.TenantAdministrators.RemoveRange(await catalog.TenantAdministrators.Where(row => row.TenantId == tenantId).ToListAsync(cancellationToken));
        catalog.TenantAdministrators.AddRange(admins.Select(admin => new TenantAdministratorRow
        {
            TenantId = tenantId,
            UserId = admin.UserId,
            Email = admin.Email,
            DisplayName = admin.DisplayName,
            Status = admin.Status,
            LastLoginDate = admin.LastLoginDate,
            InvitationExpiresDate = admin.InvitationExpiresDate,
            MeasuredDate = now,
        }));
        await catalog.SaveChangesAsync(cancellationToken);
    }

    private static string? Trim(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: var n } text && n > length ? text[..length] : value.Trim();
}

/// <summary>
/// Carries out what platform staff asked for in the console, oldest first: seeding a new organisation,
/// inviting or re-inviting an administrator, retrying a failed job.
/// </summary>
[PlatformJob("Platform sync: carries out the platform console's requests (new organisations, administrator invitations, job retries).")]
internal sealed class PlatformSyncJob(
    PlatformCatalogDbContext catalog,
    AppDbContext appDb,
    IIntegrationEventPublisher publisher,
    ITenantScopes tenants,
    IJobMonitor jobs,
    PlatformUsageRecorder recorder,
    TimeProvider timeProvider,
    ILogger<PlatformSyncJob> logger)
{
    public const string RecurringJobName = "platform-sync";

    public async Task RunAsync()
    {
        List<ProvisioningRequestRow> pending = await catalog.ProvisioningRequests
            .Where(request => request.Status == ProvisioningRequestStatus.Pending)
            .OrderBy(request => request.RequestedDate)
            .Take(50)
            .ToListAsync();

        foreach (ProvisioningRequestRow request in pending)
        {
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            try
            {
                string result = request.Kind switch
                {
                    ProvisioningRequestKind.SeedOrganisation => await SeedAsync(request, now),
                    ProvisioningRequestKind.AddAdministrator => await InTenantAsync(request, admins => admins.AddAsync(request.Email!, request.DisplayName)),
                    ProvisioningRequestKind.ResendAdministratorInvitation => await InTenantAsync(request, admins => admins.ResendInvitationAsync(request.Email!)),
                    ProvisioningRequestKind.RetryJob => jobs.Retry(request.JobId ?? string.Empty)
                        ? $"Job {request.JobId} queued again."
                        : throw new InvalidOperationException($"Job {request.JobId} is no longer waiting to be retried."),
                    _ => throw new InvalidOperationException($"Unknown request kind {request.Kind}."),
                };
                request.Complete(true, result, now);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Platform request {RequestId} ({Kind}) failed.", request.Id, request.Kind);
                request.Complete(false, exception.Message, now);
            }

            await catalog.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Raises the provisioning event in this application's outbox - every module seeds the organisation
    /// from it, and Identity emails the first administrator - and records it as seeded. Done once: a
    /// seed state already recorded means the event went out before.
    /// </summary>
    private async Task<string> SeedAsync(ProvisioningRequestRow request, DateTime now)
    {
        Guid tenantId = request.TenantId ?? throw new InvalidOperationException("A seeding request needs an organisation.");
        if (await catalog.TenantSeedStates.AnyAsync(state => state.TenantId == tenantId))
        {
            return "Already set up.";
        }

        var tenant = await catalog.Tenants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == tenantId)
            ?? throw new InvalidOperationException("The organisation no longer exists.");

        publisher.PublishForTenant(tenant.Id, new TenantProvisionedIntegrationEvent(
            tenant.Id, tenant.Code, tenant.Name, tenant.CountryCode, tenant.DefaultTimeZone,
            tenant.DefaultCurrency, tenant.DefaultLocale, tenant.DataRegion, request.Email ?? string.Empty));
        await appDb.SaveChangesAsync();

        catalog.TenantSeedStates.Add(new TenantSeedStateRow { TenantId = tenant.Id, SeededDate = now });
        return $"Setting up {tenant.Name}; the first administrator ({request.Email}) is emailed an invitation.";
    }

    private async Task<string> InTenantAsync(ProvisioningRequestRow request, Func<IOrganisationAdministrators, Task<string>> work)
    {
        Guid tenantId = request.TenantId ?? throw new InvalidOperationException("An administrator request needs an organisation.");
        string result = await tenants.RunAsync(tenantId, provider => work(provider.GetRequiredService<IOrganisationAdministrators>()));

        // Show the new state in the console straight away rather than at the next hourly report.
        IReadOnlyList<OrganisationAdministratorInfo> admins = await tenants.RunAsync(
            tenantId, provider => provider.GetRequiredService<IOrganisationAdministrators>().ListAsync());
        await recorder.SaveAdministratorsAsync(tenantId, admins, timeProvider.GetUtcNow().UtcDateTime, CancellationToken.None);
        return result;
    }
}

/// <summary>Every five minutes, tells the platform console how this application's background jobs are doing.</summary>
[PlatformJob("Job health: reports background workers, recurring and failed jobs to the platform console.")]
internal sealed class JobHealthReporter(PlatformCatalogDbContext catalog, IJobMonitor jobs, TimeProvider timeProvider)
{
    public const string RecurringJobName = "platform-job-health";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync()
    {
        JobHealthRow? row = await catalog.JobHealth.FirstOrDefaultAsync(health => health.Id == 1);
        if (row is null)
        {
            row = new JobHealthRow { Id = 1 };
            catalog.JobHealth.Add(row);
        }

        row.MeasuredDate = timeProvider.GetUtcNow().UtcDateTime;
        row.ServersJson = JsonSerializer.Serialize(jobs.Servers().Select(server => new { server.Name, Heartbeat = server.LastHeartbeat, server.Workers }), Json);
        row.RecurringJson = JsonSerializer.Serialize(jobs.RecurringJobs(), Json);
        row.FailedJson = JsonSerializer.Serialize(
            jobs.FailedJobs(50).Select(job => new { job.Id, job.Job, job.FailedAt, job.Reason, TenantId = Guid.TryParse(job.TenantId, out Guid id) ? id : (Guid?)null }),
            Json);
        await catalog.SaveChangesAsync();
    }
}

internal sealed class PlatformUsageJobRegistration(IRecurringJobManager recurringJobs, IBackgroundJobClient backgroundJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<PlatformUsageSweep>(PlatformUsageSweep.RecurringJobName, sweep => sweep.RunAsync(), "10 * * * *");
        recurringJobs.AddOrUpdate<PlatformSyncJob>(PlatformSyncJob.RecurringJobName, job => job.RunAsync(), Cron.Minutely());
        recurringJobs.AddOrUpdate<JobHealthReporter>(JobHealthReporter.RecurringJobName, job => job.RunAsync(), "*/5 * * * *");

        // Once at start too, so a fresh install has figures and health without waiting.
        backgroundJobs.Enqueue<PlatformUsageSweep>(sweep => sweep.RunAsync());
        backgroundJobs.Enqueue<JobHealthReporter>(job => job.RunAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
