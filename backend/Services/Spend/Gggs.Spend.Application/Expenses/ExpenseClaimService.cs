using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Application.Travel;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;
using Gggs.Workflow.Contracts;

namespace Gggs.Spend.Application.Expenses;

/// <summary>
/// Creating a claim, adding lines to it while it is still Draft, and submitting it for approval -
/// the same template as <c>Gggs.Time.Application.Leave.LeaveRequestService</c>. Submission starts
/// a Workflow instance (architecture.md §13.3); approval/rejection themselves happen only in
/// <c>ExpenseClaimWorkflowOutcomeConsumer</c>, reacting to that instance's outcome.
/// </summary>
public sealed class ExpenseClaimService(
    IExpenseClaimRepository claims,
    IExpenseLineRepository lines,
    IExpenseCategoryRepository categories,
    IEmployeeSnapshotRepository snapshots,
    ITravelRequestRepository travelRequests,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string ExpenseClaimWorkflowCode = "EXPENSE_CLAIM";
    private const string ExpenseClaimEntityType = "EXPENSE_CLAIM";

    public async Task<ExpenseClaim> CreateAsync(
        Guid employeeId, string claimNo, string title, string currency, Guid? travelRequestId = null,
        CancellationToken cancellationToken = default)
    {
        EmployeeSnapshot? employee = await snapshots.GetAsync(employeeId, cancellationToken);
        Guard(employee is not null, "SPEND_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "SPEND_EMPLOYEE_EXITED", "This employee has already exited.");

        Guard(
            !await claims.ClaimNoExistsAsync(claimNo, cancellationToken),
            "EXPENSE_CLAIM_NO_ALREADY_EXISTS",
            $"A claim with number '{claimNo}' already exists.");

        if (travelRequestId is { } requestId)
        {
            TravelRequest travelRequest = await travelRequests.GetAsync(requestId, cancellationToken)
                ?? throw new NotFoundException(nameof(TravelRequest), requestId);

            Guard(
                travelRequest.EmployeeId == employeeId,
                "EXPENSE_CLAIM_TRAVEL_REQUEST_NOT_OWNED",
                "A claim can only be linked to the same employee's own travel request.");
        }

        ExpenseClaim claim = ExpenseClaim.Create(claimNo, employeeId, title, currency, travelRequestId);
        claims.Add(claim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<ExpenseLine> AddLineAsync(
        Guid claimId, Guid categoryId, DateOnly expenseDate, decimal amount, string? merchant,
        CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await claims.GetAsync(claimId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseClaim), claimId);

        _ = await categories.GetAsync(categoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), categoryId);

        ExpenseLine line = ExpenseLine.Create(claimId, categoryId, expenseDate, amount, merchant);
        lines.Add(line);

        IReadOnlyList<ExpenseLine> existingLines = await lines.ListForClaimAsync(claimId, cancellationToken);
        claim.RecomputeTotal(existingLines.Sum(x => x.Amount) + amount);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return line;
    }

    public async Task<ExpenseClaim> SubmitAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await GetOrThrowAsync(claimId, cancellationToken);
        claim.Submit(clock.GetUtcNow().UtcDateTime);

        publisher.Publish(new StartWorkflowIntegrationEvent(
            ExpenseClaimWorkflowCode,
            ExpenseClaimEntityType,
            claim.Id,
            SourceService: "spend",
            InitiatorUserId: currentUser.UserId,
            SubjectEmployeeId: claim.EmployeeId,
            ContextJson: BuildWorkflowContextJson(claim),
            CorrelationId: currentUser.CorrelationId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    private static string BuildWorkflowContextJson(ExpenseClaim claim)
        => System.Text.Json.JsonSerializer.Serialize(new { total = claim.Total, currency = claim.Currency });

    private async Task<ExpenseClaim> GetOrThrowAsync(Guid claimId, CancellationToken cancellationToken)
        => await claims.GetAsync(claimId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseClaim), claimId);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
