using Gggs.BuildingBlocks.Messaging;
using Gggs.Spend.Domain.Expenses;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Spend.Application.Expenses;

/// <summary>
/// Architecture.md §13.3 step 4: Spend's own reaction to a workflow instance completing for an
/// expense claim - the same shape as <c>Gggs.Time.Application.Leave.LeaveWorkflowOutcomeConsumer</c>.
/// </summary>
public sealed class ExpenseClaimWorkflowOutcomeConsumer(
    IExpenseClaimRepository claims,
    TimeProvider clock,
    ILogger<ExpenseClaimWorkflowOutcomeConsumer> logger)
    : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
      IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    private const string ExpenseClaimEntityType = "EXPENSE_CLAIM";

    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != ExpenseClaimEntityType)
        {
            return;
        }

        ExpenseClaim? claim = await claims.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (claim is null)
        {
            logger.LogWarning("WorkflowApproved for unknown expense claim {ClaimId}.", integrationEvent.EntityId);
            return;
        }

        if (claim.Status != ExpenseClaimStatus.Submitted)
        {
            logger.LogInformation(
                "Expense claim {ClaimId} is already {Status}; ignoring a duplicate WorkflowApproved.",
                claim.Id, claim.Status);
            return;
        }

        claim.LinkWorkflowInstance(integrationEvent.InstanceId);
        claim.MarkApproved(clock.GetUtcNow().UtcDateTime);

        logger.LogInformation("Expense claim {ClaimId} approved.", claim.Id);
    }

    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != ExpenseClaimEntityType)
        {
            return;
        }

        ExpenseClaim? claim = await claims.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (claim is null || claim.Status != ExpenseClaimStatus.Submitted)
        {
            return;
        }

        claim.LinkWorkflowInstance(integrationEvent.InstanceId);
        claim.MarkRejected(clock.GetUtcNow().UtcDateTime);

        logger.LogInformation("Expense claim {ClaimId} rejected.", claim.Id);
    }
}
