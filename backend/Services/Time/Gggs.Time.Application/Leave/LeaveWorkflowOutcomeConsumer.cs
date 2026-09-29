using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Domain.Leave;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Time.Application.Leave;

/// <summary>
/// Architecture.md §13.3 step 4: Time's own reaction to a workflow instance completing. Approval
/// posts the ledger debit (the balance only actually moves once someone has said yes, not on
/// submission - <see cref="LeaveRequestService.SubmitAsync"/> only ever touches
/// <c>PendingApproval</c>); rejection just flips the request's status, since nothing was ever
/// debited for it to reverse.
/// </summary>
public sealed class LeaveWorkflowOutcomeConsumer(
    ILeaveRequestRepository requests,
    LeaveLedgerService ledgerService,
    TimeProvider clock,
    ILogger<LeaveWorkflowOutcomeConsumer> logger)
    : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
      IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    private const string LeaveRequestEntityType = "LEAVE_REQUEST";

    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != LeaveRequestEntityType)
        {
            return;
        }

        var request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null)
        {
            logger.LogWarning("WorkflowApproved for unknown leave request {LeaveRequestId}.", integrationEvent.EntityId);
            return;
        }

        if (request.Status != LeaveRequestStatus.Pending)
        {
            logger.LogInformation(
                "Leave request {LeaveRequestId} is already {Status}; ignoring a duplicate WorkflowApproved.",
                request.Id, request.Status);
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkApproved();

        await ledgerService.PostAsync(
            request.EmployeeId, request.LeaveTypeId, request.FromDate.Year,
            LeaveLedgerTransactionType.Debit, -request.TotalDays, // Debits post negative - see LeaveLedgerEntry.Days' own remarks.
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            sourceType: "LeaveRequest", sourceId: request.Id, remarks: null, cancellationToken);

        logger.LogInformation("Leave request {LeaveRequestId} approved; posted a {Days}-day debit.", request.Id, request.TotalDays);
    }

    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != LeaveRequestEntityType)
        {
            return;
        }

        var request = await requests.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (request is null || request.Status != LeaveRequestStatus.Pending)
        {
            return;
        }

        request.LinkWorkflowInstance(integrationEvent.InstanceId);
        request.MarkRejected();

        await ledgerService.RebuildBalanceAsync(request.EmployeeId, request.LeaveTypeId, request.FromDate.Year, cancellationToken);

        logger.LogInformation("Leave request {LeaveRequestId} rejected.", request.Id);
    }
}
