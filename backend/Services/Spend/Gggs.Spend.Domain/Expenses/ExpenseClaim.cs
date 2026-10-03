using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Expenses;

public enum ExpenseClaimStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>
/// One employee's expense claim - a header (this) over one or more <see cref="ExpenseLine"/>s.
/// Goes through <c>Gggs.Workflow</c> for approval (architecture.md §13.3), never approves itself -
/// the same shape as <c>Gggs.Time.Domain.Leave.LeaveRequest</c>. <see cref="Status"/> stays
/// <see cref="ExpenseClaimStatus.Submitted"/> until <c>ExpenseClaimWorkflowOutcomeConsumer</c>
/// reacts to a <c>WorkflowApproved</c>/<c>RejectedIntegrationEvent</c> - this aggregate has no
/// <c>Approve()</c>/<c>Reject()</c> a caller in this service could invoke directly, precisely so
/// the only path to "Approved" is through the workflow that was actually run. No multi-currency or
/// reimbursement mode in this v1 slice (architecture.md's <c>exp.ExpenseClaim</c>) -
/// <see cref="Total"/> is a plain sum of this claim's lines, recomputed whenever a line is added.
/// <see cref="TravelRequestId"/> is optional: most claims (meals, supplies) are not against a trip,
/// but a claim raised for travel expenses links back to its <c>Travel.TravelRequest</c> so
/// <c>Travel.TravelSettlement</c> can sum the actual spend against that trip's advance.
/// </summary>
public sealed class ExpenseClaim : TenantAggregateRoot
{
    private ExpenseClaim()
    {
        ClaimNo = null!;
        Title = null!;
        Currency = null!;
    }

    public string ClaimNo { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Title { get; private set; }

    public string Currency { get; private set; }

    public decimal Total { get; private set; }

    public ExpenseClaimStatus Status { get; private set; }

    public DateTime? SubmittedDate { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public Guid? TravelRequestId { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public static ExpenseClaim Create(
        string claimNo, Guid employeeId, string title, string currency, Guid? travelRequestId = null) => new()
        {
            ClaimNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(claimNo).Trim(), 30),
            EmployeeId = Guard.NotEmpty(employeeId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            Total = 0,
            Status = ExpenseClaimStatus.Draft,
            TravelRequestId = travelRequestId,
            IsActive = true,
        };

    /// <summary>Recomputes <see cref="Total"/> from the claim's current lines - called after every
    /// line addition, since a line's amount is denormalized onto the header for cheap listing.
    /// Only valid while the claim is still Draft, the same window lines may be added in.</summary>
    public void RecomputeTotal(decimal total)
    {
        Guard.Against(Status != ExpenseClaimStatus.Draft, "EXPENSE_CLAIM_NOT_DRAFT", $"Claim '{ClaimNo}' must be Draft to add lines; it is {Status}.");
        Total = total;
    }

    public void Submit(DateTime submittedAtUtc)
    {
        Guard.Against(Status != ExpenseClaimStatus.Draft, "EXPENSE_CLAIM_NOT_DRAFT", $"Claim '{ClaimNo}' must be Draft to submit; it is {Status}.");
        Guard.Against(Total <= 0, "EXPENSE_CLAIM_HAS_NO_LINES", $"Claim '{ClaimNo}' has no lines to submit.");

        Status = ExpenseClaimStatus.Submitted;
        SubmittedDate = submittedAtUtc;
    }

    /// <summary>Set once the workflow instance actually starts - see
    /// <c>ExpenseClaimService.SubmitAsync</c>'s own remarks on why this is a separate step from
    /// <see cref="Submit"/> rather than folded into it.</summary>
    public void LinkWorkflowInstance(Guid workflowInstanceId) => WorkflowInstanceId = Guard.NotEmpty(workflowInstanceId);

    public void MarkApproved(DateTime decidedAtUtc)
    {
        Guard.Against(Status != ExpenseClaimStatus.Submitted, "EXPENSE_CLAIM_NOT_SUBMITTED", $"Claim '{ClaimNo}' must be Submitted to approve; it is {Status}.");
        Status = ExpenseClaimStatus.Approved;
        DecidedDate = decidedAtUtc;
    }

    public void MarkRejected(DateTime decidedAtUtc)
    {
        Guard.Against(Status != ExpenseClaimStatus.Submitted, "EXPENSE_CLAIM_NOT_SUBMITTED", $"Claim '{ClaimNo}' must be Submitted to reject; it is {Status}.");
        Status = ExpenseClaimStatus.Rejected;
        DecidedDate = decidedAtUtc;
    }
}

/// <summary>One line of an <see cref="ExpenseClaim"/> - a date, a category, an amount and an
/// optional merchant name. No receipt attachment, policy violation flags or per-line approval in
/// this v1 slice (architecture.md's <c>exp.ExpenseLine</c>).</summary>
public sealed class ExpenseLine : TenantAggregateRoot
{
    private ExpenseLine()
    {
        Merchant = null!;
    }

    public Guid ClaimId { get; private set; }

    public Guid CategoryId { get; private set; }

    public DateOnly ExpenseDate { get; private set; }

    public decimal Amount { get; private set; }

    public string? Merchant { get; private set; }

    public static ExpenseLine Create(Guid claimId, Guid categoryId, DateOnly expenseDate, decimal amount, string? merchant)
    {
        Guard.Against(amount <= 0, "EXPENSE_LINE_AMOUNT_NOT_POSITIVE", "The amount must be positive.");

        return new ExpenseLine
        {
            ClaimId = Guard.NotEmpty(claimId),
            CategoryId = Guard.NotEmpty(categoryId),
            ExpenseDate = expenseDate,
            Amount = amount,
            Merchant = merchant is null ? null : Guard.MaxLength(merchant.Trim(), 200),
            IsActive = true,
        };
    }
}
