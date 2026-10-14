using Gggs.BuildingBlocks.Jobs;
using Hangfire;

namespace Gggs.Modules.Workflow.Application;

/// <summary>Every ten minutes, queues one SLA check per active tenant.</summary>
[PlatformJob("Workflow SLA sweep: fans out one escalation check per active tenant.")]
internal sealed class WorkflowSlaSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "workflow-sla-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<WorkflowSlaJob>((job, tenantId) => job.RunAsync(tenantId));
}

/// <summary>Escalates one tenant's overdue tasks. The tenant is restored by the job activator before this runs.</summary>
internal sealed class WorkflowSlaJob(WorkflowEngine engine, ILogger<WorkflowSlaJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int escalated = await engine.EscalateOverdueAsync(CancellationToken.None);
        if (escalated > 0)
        {
            logger.LogInformation("Escalated {Count} overdue approval task(s) for tenant {TenantId}.", escalated, tenantId);
        }
    }
}

/// <summary>Puts the sweep on the worker's recurring schedule at startup. Idempotent.</summary>
internal sealed class WorkflowJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<WorkflowSlaSweep>(WorkflowSlaSweep.RecurringJobName, sweep => sweep.RunAsync(), "*/10 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
