using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Domain.Attendance;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Overtime's reaction to a workflow instance completing - the same shape as
/// <c>AttendanceWorkflowOutcomeConsumer</c>, one module over. Approval grants the full
/// <see cref="OvertimeRequest.RequestedMinutes"/>: the generic <c>WorkflowApprovedIntegrationEvent</c>
/// carries no per-entity override field for a partial grant, and this v1 slice has no approver UI to
/// capture one - see architecture.md's own remarks on <c>OvertimeRequest.ApprovedMinutes</c> existing
/// for a future manager-adjusts-the-amount flow, not this one.
/// </summary>
public sealed class OvertimeWorkflowOutcomeConsumer(
    IOvertimeRequestRepository requests, ILogger<OvertimeWorkflowOutcomeConsumer> logger)
    : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
      IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    private const string OvertimeEntityType = "OVERTIME_REQUEST";

    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != OvertimeEntityType)
        {
            return;
        }

        OvertimeRequest? request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null)
        {
            logger.LogWarning("WorkflowApproved for unknown overtime request {OvertimeRequestId}.", integrationEvent.EntityId);
            return;
        }

        if (request.Status != OvertimeRequestStatus.Pending)
        {
            logger.LogInformation(
                "Overtime request {OvertimeRequestId} is already {Status}; ignoring a duplicate WorkflowApproved.",
                request.Id, request.Status);
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkApproved(request.RequestedMinutes);

        logger.LogInformation("Overtime request {OvertimeRequestId} approved for {OvertimeDate}.", request.Id, request.OvertimeDate);
    }

    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != OvertimeEntityType)
        {
            return;
        }

        OvertimeRequest? request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null || request.Status != OvertimeRequestStatus.Pending)
        {
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkRejected();

        logger.LogInformation("Overtime request {OvertimeRequestId} rejected.", request.Id);
    }
}
