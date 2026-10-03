using Gggs.BuildingBlocks.Messaging;
using Gggs.Spend.Domain.Travel;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Spend.Application.Travel;

/// <summary>
/// Architecture.md §13.3 step 4: Spend's own reaction to a workflow instance completing for a
/// travel request - the same shape as <c>ExpenseClaimWorkflowOutcomeConsumer</c>.
/// </summary>
public sealed class TravelRequestWorkflowOutcomeConsumer(
    ITravelRequestRepository requests,
    TimeProvider clock,
    ILogger<TravelRequestWorkflowOutcomeConsumer> logger)
    : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
      IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    private const string TravelRequestEntityType = "TRAVEL_REQUEST";

    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != TravelRequestEntityType)
        {
            return;
        }

        TravelRequest? request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null)
        {
            logger.LogWarning("WorkflowApproved for unknown travel request {TravelRequestId}.", integrationEvent.EntityId);
            return;
        }

        if (request.Status != TravelRequestStatus.Submitted)
        {
            logger.LogInformation(
                "Travel request {TravelRequestId} is already {Status}; ignoring a duplicate WorkflowApproved.",
                request.Id, request.Status);
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkApproved(clock.GetUtcNow().UtcDateTime);

        logger.LogInformation("Travel request {TravelRequestId} approved.", request.Id);
    }

    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != TravelRequestEntityType)
        {
            return;
        }

        TravelRequest? request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null || request.Status != TravelRequestStatus.Submitted)
        {
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkRejected(clock.GetUtcNow().UtcDateTime);

        logger.LogInformation("Travel request {TravelRequestId} rejected.", request.Id);
    }
}
