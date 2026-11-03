using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Recruitment.Contracts;
using Gggs.Modules.Studio.Domain;
using Gggs.Modules.Studio.Infrastructure;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Studio.Application;

/// <summary>
/// Runs automations: an event arrives, each active automation on its trigger whose conditions
/// hold gets one delivery (never two for the same event), and the delivery is attempted at once.
/// A failed attempt waits for <see cref="AutomationRetrySweep"/>; one that keeps failing is
/// abandoned for someone to retry by hand.
/// </summary>
internal sealed class AutomationEngine(
    AppDbContext dbContext,
    TriggerFieldResolver resolver,
    IIntegrationConnections connections,
    TimeProvider timeProvider,
    ILogger<AutomationEngine> logger)
{
    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (StudioTriggers.For(integrationEvent.GetType()) is not { } trigger)
        {
            return;
        }

        List<Automation> automations = await dbContext.Automations.AsNoTracking()
            .Where(row => row.IsActive && row.TriggerCode == trigger.Code)
            .ToListAsync(cancellationToken);
        if (automations.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, string> fields = await resolver.ResolveAsync(integrationEvent, cancellationToken);
        List<Guid> ids = [.. automations.Select(row => row.Id)];
        HashSet<Guid> done = [.. await dbContext.AutomationDeliveries.AsNoTracking()
            .Where(row => row.EventId == integrationEvent.MessageId && ids.Contains(row.AutomationId))
            .Select(row => row.AutomationId)
            .ToListAsync(cancellationToken)];

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        List<AutomationDelivery> queued = [];
        foreach (Automation automation in automations.Where(row => !done.Contains(row.Id) && AutomationRules.Matches(row.Conditions, fields)))
        {
            AutomationDelivery delivery = AutomationDelivery.Queue(automation.Id, integrationEvent.MessageId, trigger.Code, AutomationRules.RenderAll(automation.Parameters, fields), now);
            dbContext.AutomationDeliveries.Add(delivery);
            queued.Add(delivery);
        }

        if (queued.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        Dictionary<Guid, Automation> byId = automations.ToDictionary(row => row.Id);
        foreach (AutomationDelivery delivery in queued)
        {
            await AttemptAsync(delivery, byId[delivery.AutomationId], cancellationToken);
        }
    }

    /// <summary>Every delivery whose next attempt is due, oldest first; a slow outside system holds up only its own.</summary>
    public async Task<int> RetryDueAsync(CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        // A pending delivery is normally being attempted by the consumer that queued it; only one
        // left pending for a while (the worker stopped mid-way) is picked up here.
        DateTime stuck = now.AddMinutes(-2);
        List<AutomationDelivery> due = await dbContext.AutomationDeliveries
            .Where(row => (row.Status == DeliveryStatus.Failed && row.NextAttemptAt <= now) || (row.Status == DeliveryStatus.Pending && row.NextAttemptAt <= stuck))
            .OrderBy(row => row.NextAttemptAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        List<Guid> ids = [.. due.Select(row => row.AutomationId).Distinct()];
        Dictionary<Guid, Automation> automations = await dbContext.Automations.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (AutomationDelivery delivery in due)
        {
            if (automations.GetValueOrDefault(delivery.AutomationId) is { IsActive: false })
            {
                // Switching an automation off stops what it was still trying to send; Retry can still send it by hand.
                delivery.Abandon("The automation was switched off.", now);
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            await AttemptAsync(delivery, automations.GetValueOrDefault(delivery.AutomationId), cancellationToken);
        }

        return due.Count;
    }

    /// <summary>One attempt, recorded whatever happens; a deleted automation's delivery is abandoned rather than sent.</summary>
    public async Task AttemptAsync(AutomationDelivery delivery, Automation? automation, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (automation is null)
        {
            delivery.Abandon("The automation was deleted.", now);
        }
        else
        {
            ConnectionRunResult result = await connections.RunAsync(automation.ConnectionId, automation.Operation, delivery.Payload, cancellationToken);
            delivery.Record(result.Succeeded, result.Message, timeProvider.GetUtcNow().UtcDateTime);
            if (!result.Succeeded)
            {
                logger.LogWarning("Automation {AutomationId} failed attempt {Attempt}: {Message}", automation.Id, delivery.Attempts, result.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Hands every event an automation can start from to the engine.</summary>
internal sealed class AutomationTriggers(AutomationEngine engine) :
    IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>,
    IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>,
    IIntegrationEventConsumer<EmployeeAssignmentChangedIntegrationEvent>,
    IIntegrationEventConsumer<DepartmentHeadChangedIntegrationEvent>,
    IIntegrationEventConsumer<LeaveApprovedIntegrationEvent>,
    IIntegrationEventConsumer<LeaveCancelledIntegrationEvent>,
    IIntegrationEventConsumer<TimesheetApprovedIntegrationEvent>,
    IIntegrationEventConsumer<WorkflowTaskAssignedIntegrationEvent>,
    IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
    IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>,
    IIntegrationEventConsumer<PayrollRunFinalizedIntegrationEvent>,
    IIntegrationEventConsumer<PayrollRunPaidIntegrationEvent>,
    IIntegrationEventConsumer<CandidateHiredIntegrationEvent>,
    IIntegrationEventConsumer<DocumentSignedIntegrationEvent>,
    IIntegrationEventConsumer<ReviewReleasedIntegrationEvent>,
    IIntegrationEventConsumer<CourseCompletedIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(EmployeeAssignmentChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(DepartmentHeadChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(LeaveApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(LeaveCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(TimesheetApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(WorkflowTaskAssignedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(PayrollRunFinalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(PayrollRunPaidIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(CandidateHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(DocumentSignedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(ReviewReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(CourseCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) => engine.HandleAsync(integrationEvent, cancellationToken);
}

/// <summary>Every minute, queues one retry pass per active tenant for deliveries and directory accounts whose next attempt is due.</summary>
[PlatformJob("Automations: fans out one retry pass per active tenant for failed deliveries and directory accounts that are due again.")]
internal sealed class AutomationRetrySweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "studio-automation-retry";

    public Task RunAsync() => fanOut.FanOutAsync<AutomationRetryJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class AutomationRetryJob(AutomationEngine engine, AccountEngine accounts)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        await engine.RetryDueAsync(CancellationToken.None);
        await accounts.RetryDueAsync(CancellationToken.None);
    }
}

internal sealed class StudioJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<AutomationRetrySweep>(AutomationRetrySweep.RecurringJobName, sweep => sweep.RunAsync(), "* * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
