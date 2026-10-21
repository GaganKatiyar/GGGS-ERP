using System.Text.Json;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Compliance.Domain;
using Gggs.Modules.Compliance.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compliance.Application;

/// <summary>
/// Seeds India's statutory rules (as published version 1) and the usual filing obligations into a
/// tenant. Codes already present are left alone, so it can run again safely.
/// </summary>
public sealed class ComplianceSeeder(AppDbContext dbContext, ITenantClock clock, TimeProvider timeProvider)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> rules = [.. await dbContext.StatutoryRules
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(rule => rule.Code)
            .Distinct()
            .ToListAsync(cancellationToken)];
        HashSet<string> obligations = [.. await dbContext.ComplianceObligations
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(obligation => obligation.Code)
            .ToListAsync(cancellationToken)];

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        int added = 0;
        foreach (RuleSeed seed in IndiaStatutoryDefaults.Rules.Where(seed => !rules.Contains(seed.Code)))
        {
            string configuration = StatutoryConfigValidator.Normalise(seed.Type, JsonSerializer.Serialize(seed.Configuration, seed.Configuration.GetType(), StatutoryJson.Options));
            var rule = StatutoryRule.Draft(seed.Code, seed.Name, seed.Type, "IN", seed.StateCode, 1, seed.EffectiveFrom, configuration, seed.Eligibility, "Seeded with the scheme as published; review before the first payroll.");
            rule.Publish(now);
            dbContext.StatutoryRules.Add(rule);
            added++;
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        foreach (ObligationSeed seed in IndiaStatutoryDefaults.Obligations.Where(seed => !obligations.Contains(seed.Code)))
        {
            dbContext.ComplianceObligations.Add(ComplianceObligation.Create(seed.Code, seed.Details, new DateOnly(today.Year, today.Month, 1)));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

internal sealed class SeedComplianceOnTenantProvisioned(ComplianceSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>Fans the daily compliance sweep out to every tenant.</summary>
[PlatformJob("Compliance sweep: fans out one compliance check per active tenant.")]
internal sealed class ComplianceSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "compliance-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<ComplianceSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

/// <summary>Generates the coming filings and sends the day's reminders for one tenant.</summary>
internal sealed class ComplianceSweepJob(ComplianceCalendar calendar, ILogger<ComplianceSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int generated = await calendar.GenerateAsync(CancellationToken.None);
        int reminded = await calendar.RemindAsync(CancellationToken.None);
        if (generated + reminded > 0)
        {
            logger.LogInformation("Compliance sweep for tenant {TenantId}: {Generated} filing(s) generated, {Reminded} reminder(s) sent.", tenantId, generated, reminded);
        }
    }
}

internal sealed class ComplianceJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<ComplianceSweep>(ComplianceSweep.RecurringJobName, sweep => sweep.RunAsync(), "15 3 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
