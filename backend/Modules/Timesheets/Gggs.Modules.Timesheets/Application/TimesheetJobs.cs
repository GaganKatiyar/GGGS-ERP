using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Timesheets.Application;

/// <summary>Fans the weekly missing-timesheet reminder out to every tenant.</summary>
[PlatformJob("Timesheet reminders: fans out one reminder pass per active tenant.")]
internal sealed class TimesheetReminderSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "timesheet-reminders";

    public Task RunAsync() => fanOut.FanOutAsync<TimesheetReminderJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class TimesheetReminderJob(TimesheetService timesheets, ILogger<TimesheetReminderJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int reminded = await timesheets.RemindMissingAsync(CancellationToken.None);
        if (reminded > 0)
        {
            logger.LogInformation("Timesheet reminders for tenant {TenantId}: {Count} person(s) have not submitted last week.", tenantId, reminded);
        }
    }
}

internal sealed class TimesheetJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Monday morning (UTC): last week should be in by now.
        recurringJobs.AddOrUpdate<TimesheetReminderSweep>(TimesheetReminderSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 4 * * 1");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ApproveTimesheetOnApproval(TimesheetService timesheets) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == TimesheetService.SubjectType)
        {
            await timesheets.ApplyDecisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectTimesheetOnRejection(TimesheetService timesheets) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == TimesheetService.SubjectType)
        {
            await timesheets.ApplyDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}

internal sealed class ReturnTimesheetOnSendBack(TimesheetService timesheets) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == TimesheetService.SubjectType)
        {
            await timesheets.ApplyDecisionAsync(integrationEvent.SubjectId, approved: null, cancellationToken);
        }
    }
}
