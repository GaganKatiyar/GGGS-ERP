using Gggs.BuildingBlocks.Messaging;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// The consumer side of architecture.md §13.3 step 1: a source service's <c>StartWorkflow</c>
/// command, landing here from its own outbox.
/// </summary>
public sealed class StartWorkflowConsumer(WorkflowEngine engine, ILogger<StartWorkflowConsumer> logger)
    : IIntegrationEventConsumer<StartWorkflowIntegrationEvent>
{
    public async Task HandleAsync(StartWorkflowIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await engine.StartAsync(integrationEvent, cancellationToken);

        logger.LogInformation(
            "Started a workflow instance from {SourceService}'s {EntityType} {EntityId}.",
            integrationEvent.SourceService, integrationEvent.EntityType, integrationEvent.EntityId);
    }
}
