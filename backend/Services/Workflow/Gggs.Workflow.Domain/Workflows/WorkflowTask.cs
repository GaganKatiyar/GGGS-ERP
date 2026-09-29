using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Workflow.Domain.Workflows;

/// <summary>
/// One approver's pending (or acted-on) work item for one step of a <see cref="WorkflowInstance"/>.
/// A step with three "Any" approvers creates three tasks; the first action any of them takes
/// resolves the step, and <c>WorkflowEngine</c> cancels the other two rather than leaving them
/// dangling in someone's inbox.
/// </summary>
public sealed class WorkflowTask : TenantAggregateRoot
{
    private WorkflowTask()
    {
        StepKey = null!;
    }

    public Guid InstanceId { get; private set; }

    public string StepKey { get; private set; }

    public Guid AssigneeUserId { get; private set; }

    /// <summary>Set only when <see cref="AssigneeUserId"/> is a delegate standing in for the
    /// resolved approver - the original approver, kept for the trail
    /// (<c>WorkflowActionLog</c> records who actually acted, this records who it was really
    /// meant for).</summary>
    public Guid? OriginalAssigneeUserId { get; private set; }

    public WorkflowTaskStatus Status { get; private set; }

    public DateTime? DueDate { get; private set; }

    public DateTime? ActedDate { get; private set; }

    public string? Comment { get; private set; }

    public static WorkflowTask Create(
        Guid instanceId, string stepKey, Guid assigneeUserId, Guid? originalAssigneeUserId, DateTime? dueDate) => new()
        {
            InstanceId = Guard.NotEmpty(instanceId),
            StepKey = Guard.NotNullOrWhiteSpace(stepKey),
            AssigneeUserId = Guard.NotEmpty(assigneeUserId),
            OriginalAssigneeUserId = originalAssigneeUserId,
            Status = WorkflowTaskStatus.Pending,
            DueDate = dueDate,
            IsActive = true,
        };

    public void Act(WorkflowTaskStatus outcome, string? comment, DateTime now)
    {
        Guard.Against(Status != WorkflowTaskStatus.Pending, "WORKFLOW_TASK_NOT_PENDING", "This task has already been acted on.");
        Guard.Against(
            outcome is not (WorkflowTaskStatus.Approved or WorkflowTaskStatus.Rejected or WorkflowTaskStatus.SentBack),
            "WORKFLOW_TASK_INVALID_OUTCOME",
            "Acting on a task requires approving, rejecting or sending it back.");

        Status = outcome;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 1000);
        ActedDate = now;
    }

    /// <summary>The step resolved through a sibling task (another "Any" approver acted first, or
    /// the instance was cancelled) - this one is closed without anyone acting on it.</summary>
    public void Cancel(DateTime now)
    {
        if (Status != WorkflowTaskStatus.Pending)
        {
            return;
        }

        Status = WorkflowTaskStatus.Cancelled;
        ActedDate = now;
    }

    public void Escalate(DateTime now)
    {
        Guard.Against(Status != WorkflowTaskStatus.Pending, "WORKFLOW_TASK_NOT_PENDING", "Only a pending task can escalate.");
        Status = WorkflowTaskStatus.Escalated;
        ActedDate = now;
    }

    public void Reassign(Guid newAssigneeUserId)
    {
        Guard.Against(Status != WorkflowTaskStatus.Pending, "WORKFLOW_TASK_NOT_PENDING", "Only a pending task can be reassigned.");
        AssigneeUserId = Guard.NotEmpty(newAssigneeUserId);
    }
}

public enum WorkflowTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    SentBack = 4,
    Escalated = 5,
    Cancelled = 6,
}

/// <summary>
/// Append-only trail of everything acted on a <see cref="WorkflowInstance"/> - architecture.md
/// §16.1's "business/action audit", specific to workflow. Distinct from the tenant-wide
/// <c>audit.AuditLog</c> (which records field-level changes to this service's own aggregates, not
/// approval decisions) - this is what an instance's own timeline view reads.
/// </summary>
public sealed class WorkflowActionLog : TenantAggregateRoot
{
    private WorkflowActionLog()
    {
        Action = null!;
    }

    public Guid InstanceId { get; private set; }

    public Guid? TaskId { get; private set; }

    public string Action { get; private set; }

    public Guid ActorUserId { get; private set; }

    /// <summary>Set when the actor acted as someone else's delegate.</summary>
    public Guid? OnBehalfOfUserId { get; private set; }

    public string? Comment { get; private set; }

    public DateTime OccurredDate { get; private set; }

    public static WorkflowActionLog Record(
        Guid instanceId, Guid? taskId, string action, Guid actorUserId, Guid? onBehalfOfUserId, string? comment, DateTime now) => new()
        {
            InstanceId = Guard.NotEmpty(instanceId),
            TaskId = taskId,
            Action = Guard.MaxLength(Guard.NotNullOrWhiteSpace(action).Trim(), 50),
            ActorUserId = Guard.NotEmpty(actorUserId),
            OnBehalfOfUserId = onBehalfOfUserId,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 1000),
            OccurredDate = now,
            IsActive = true,
        };
}
