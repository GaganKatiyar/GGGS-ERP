using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Contract.Application;

/// <summary>Fans the daily contract sweep out to every tenant.</summary>
internal sealed class ContractSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "contract-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<ContractSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class ContractSweepJob(ContractService contracts, ILogger<ContractSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        (int expired, int reminded) = await contracts.SweepAsync(CancellationToken.None);
        if (expired + reminded > 0)
        {
            logger.LogInformation("Contract sweep for tenant {TenantId}: {Expired} expired or rolled on, {Reminded} renewal reminder(s).", tenantId, expired, reminded);
        }
    }
}

internal sealed class ContractJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<ContractSweep>(ContractSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Activates a contract once its approval completes.</summary>
internal sealed class ActivateContractOnApproval(ContractService contracts) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ContractService.SubjectType)
        {
            await contracts.CompleteApprovedAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnContractOnRejection(ContractService contracts) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ContractService.SubjectType)
        {
            await contracts.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnContractOnSendBack(ContractService contracts) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ContractService.SubjectType)
        {
            await contracts.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}

internal sealed class ReturnContractOnWithdrawal(ContractService contracts) : IIntegrationEventConsumer<WorkflowWithdrawnIntegrationEvent>
{
    public async Task HandleAsync(WorkflowWithdrawnIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ContractService.SubjectType)
        {
            await contracts.ReturnToDraftAsync(integrationEvent.SubjectId, cancellationToken);
        }
    }
}
