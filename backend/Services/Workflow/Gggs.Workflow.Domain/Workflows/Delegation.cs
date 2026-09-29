using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Workflow.Domain.Workflows;

/// <summary>
/// "While I am out, send my approvals to someone else" - a time-boxed stand-in for every workflow
/// (v1 has no per-definition delegation, matching <c>WorkflowStep.AllowDelegation</c> being a
/// step-level toggle the engine does not yet check; a delegation here applies to every task
/// resolved to <see cref="FromUserId"/> for the whole window).
/// </summary>
public sealed class Delegation : TenantAggregateRoot
{
    private Delegation()
    {
    }

    public Guid FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public string? Reason { get; private set; }

    public static Delegation Create(Guid fromUserId, Guid toUserId, DateOnly startDate, DateOnly endDate, string? reason)
    {
        Guard.Against(fromUserId == toUserId, "WORKFLOW_DELEGATION_SELF", "You cannot delegate to yourself.");
        Guard.Against(endDate < startDate, "WORKFLOW_DELEGATION_INVALID_RANGE", "The end date cannot be before the start date.");

        return new Delegation
        {
            FromUserId = Guard.NotEmpty(fromUserId),
            ToUserId = Guard.NotEmpty(toUserId),
            StartDate = startDate,
            EndDate = endDate,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500),
            IsActive = true,
        };
    }

    public bool IsActiveOn(DateOnly date) => IsActive && date >= StartDate && date <= EndDate;

    public void Revoke() => IsActive = false;
}
