using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Workflow.Domain;

/// <summary>
/// How a kind of request is approved, for one tenant. Edits bump <see cref="Version"/>; running
/// requests keep the stages they started with (a copy lives on the instance), so changing a
/// definition never moves the goalposts for someone already waiting.
/// </summary>
public sealed class WorkflowDefinition : TenantAggregateRoot
{
    private WorkflowDefinition()
    {
        Code = null!;
        Name = null!;
        SubjectType = null!;
        StagesJson = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string SubjectType { get; private set; }

    public string StagesJson { get; private set; }

    public int Version { get; private set; }

    /// <summary>Who approves when a stage resolves to nobody (no manager, no department head).</summary>
    public Guid? FallbackApproverEmployeeId { get; private set; }

    public IReadOnlyList<WorkflowStage> Stages => StageJson.Read(StagesJson);

    public static WorkflowDefinition Create(
        string code,
        string name,
        string? description,
        string subjectType,
        IReadOnlyList<WorkflowStage> stages,
        Guid? fallbackApproverEmployeeId)
    {
        StageJson.Validate(stages);
        return new WorkflowDefinition
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 1000),
            SubjectType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subjectType).Trim().ToLowerInvariant(), 64),
            StagesJson = StageJson.Write(stages),
            Version = 1,
            FallbackApproverEmployeeId = fallbackApproverEmployeeId,

            // A new definition starts inactive: switching approval on for a subject is its own decision.
            IsActive = false,
        };
    }

    public void Update(string name, string? description, IReadOnlyList<WorkflowStage> stages, Guid? fallbackApproverEmployeeId)
    {
        StageJson.Validate(stages);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 1000);
        StagesJson = StageJson.Write(stages);
        FallbackApproverEmployeeId = fallbackApproverEmployeeId;
        Version++;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum WorkflowInstanceStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    SentBack = 4,
    Withdrawn = 5,
}

/// <summary>One request going through approval.</summary>
public sealed class WorkflowInstance : TenantAggregateRoot
{
    private WorkflowInstance()
    {
        SubjectType = null!;
        Title = null!;
        FieldsJson = null!;
        StagesJson = null!;
    }

    public Guid? DefinitionId { get; private set; }

    public int DefinitionVersion { get; private set; }

    public string SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public string Title { get; private set; }

    public string? Link { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public string FieldsJson { get; private set; }

    /// <summary>The definition's stages as they were when the request started.</summary>
    public string StagesJson { get; private set; }

    public WorkflowInstanceStatus Status { get; private set; }

    /// <summary>Index of the stage awaiting decisions; -1 when none is.</summary>
    public int CurrentStage { get; private set; } = -1;

    /// <summary>Starts at 1; each resubmission after a send-back is a new round.</summary>
    public int Round { get; private set; } = 1;

    public DateTime SubmittedDate { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    /// <summary>The definition's fallback approver, copied at start like its stages.</summary>
    public Guid? FallbackApproverEmployeeId { get; private set; }

    /// <summary>
    /// Touched by every decision. Two approvers deciding at the same moment then conflict on the row
    /// version instead of each seeing the other's task still pending and neither closing the stage.
    /// </summary>
    public DateTime LastActivityDate { get; private set; }

    public IReadOnlyList<WorkflowStage> Stages => StageJson.Read(StagesJson);

    public IReadOnlyDictionary<string, string> Fields => StageJson.ReadFields(FieldsJson);

    public bool IsOpen => Status is WorkflowInstanceStatus.Pending or WorkflowInstanceStatus.SentBack;

    public static WorkflowInstance Start(
        WorkflowDefinition? definition,
        string subjectType,
        Guid subjectId,
        string title,
        string? link,
        Guid? requesterEmployeeId,
        Guid requestedByUserId,
        IReadOnlyDictionary<string, string> fields,
        DateTime now) => new()
        {
            DefinitionId = definition?.Id,
            DefinitionVersion = definition?.Version ?? 0,
            SubjectType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subjectType).Trim().ToLowerInvariant(), 64),
            SubjectId = Guard.NotEmpty(subjectId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 300),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.MaxLength(link.Trim(), 300),
            RequesterEmployeeId = requesterEmployeeId,
            RequestedByUserId = requestedByUserId,
            FieldsJson = StageJson.WriteFields(fields),
            StagesJson = definition?.StagesJson ?? "[]",
            Status = WorkflowInstanceStatus.Pending,
            SubmittedDate = now,
            LastActivityDate = now,
            FallbackApproverEmployeeId = definition?.FallbackApproverEmployeeId,
        };

    public void Touch(DateTime now) => LastActivityDate = now;

    public void EnterStage(int index) => CurrentStage = index;

    public void Approve(DateTime now) => Close(WorkflowInstanceStatus.Approved, now);

    public void Reject(DateTime now) => Close(WorkflowInstanceStatus.Rejected, now);

    public void Withdraw(DateTime now) => Close(WorkflowInstanceStatus.Withdrawn, now);

    public void SendBack()
    {
        EnsureStatus(WorkflowInstanceStatus.Pending);
        Status = WorkflowInstanceStatus.SentBack;
        CurrentStage = -1;
    }

    public void Resubmit(DateTime now)
    {
        EnsureStatus(WorkflowInstanceStatus.SentBack);
        Status = WorkflowInstanceStatus.Pending;
        Round++;
        SubmittedDate = now;
    }

    private void Close(WorkflowInstanceStatus status, DateTime now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("WORKFLOW_CLOSED", "This request has already been decided.");
        }

        Status = status;
        CurrentStage = -1;
        CompletedDate = now;
    }

    private void EnsureStatus(WorkflowInstanceStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("WORKFLOW_STATE", $"This request is {Status}, not {expected}.");
        }
    }
}

public enum WorkflowTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    SentBack = 4,

    /// <summary>No longer needed: another approver in an Any stage answered, or the request closed.</summary>
    Closed = 5,

    /// <summary>Handed to someone else - escalated past its SLA, or reassigned by an administrator.</summary>
    Reassigned = 6,
}

/// <summary>One approver's part in one stage of a request.</summary>
public sealed class WorkflowTask : TenantAggregateRoot
{
    private WorkflowTask()
    {
        StageName = null!;
    }

    public Guid InstanceId { get; private set; }

    public int Round { get; private set; }

    public int StageIndex { get; private set; }

    public string StageName { get; private set; }

    public Guid AssigneeEmployeeId { get; private set; }

    /// <summary>Set when the task came to its assignee through a delegation.</summary>
    public Guid? DelegatedFromEmployeeId { get; private set; }

    public WorkflowTaskStatus Status { get; private set; }

    public DateTime? DueDate { get; private set; }

    public DateTime? EscalatedDate { get; private set; }

    public DateTime? ActedDate { get; private set; }

    public Guid? ActedByUserId { get; private set; }

    public string? Comment { get; private set; }

    public bool IsPending => Status == WorkflowTaskStatus.Pending;

    public static WorkflowTask Assign(
        WorkflowInstance instance,
        int stageIndex,
        string stageName,
        Guid assigneeEmployeeId,
        Guid? delegatedFromEmployeeId,
        DateTime? dueDate) => new()
        {
            InstanceId = Guard.NotNull(instance).Id,
            Round = instance.Round,
            StageIndex = stageIndex,
            StageName = Guard.MaxLength(stageName, 100),
            AssigneeEmployeeId = Guard.NotEmpty(assigneeEmployeeId),
            DelegatedFromEmployeeId = delegatedFromEmployeeId,
            Status = WorkflowTaskStatus.Pending,
            DueDate = dueDate,
        };

    public void Decide(WorkflowTaskStatus decision, Guid actedByUserId, string? comment, DateTime now)
    {
        if (decision is not (WorkflowTaskStatus.Approved or WorkflowTaskStatus.Rejected or WorkflowTaskStatus.SentBack))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "Only approve, reject and send back are decisions.");
        }

        EnsurePending();
        Status = decision;
        ActedByUserId = actedByUserId;
        ActedDate = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 1000);
    }

    public void Close(DateTime now)
    {
        if (IsPending)
        {
            Status = WorkflowTaskStatus.Closed;
            ActedDate = now;
        }
    }

    public void HandOver(DateTime now, bool escalated)
    {
        EnsurePending();
        Status = WorkflowTaskStatus.Reassigned;
        ActedDate = now;
        if (escalated)
        {
            EscalatedDate = now;
        }
    }

    /// <summary>Overdue but with nobody to escalate to: noted once, so the sweep does not retry it forever.</summary>
    public void MarkEscalated(DateTime now) => EscalatedDate ??= now;

    private void EnsurePending()
    {
        if (!IsPending)
        {
            throw new DomainRuleException("WORKFLOW_TASK_CLOSED", "This task has already been dealt with.");
        }
    }
}

public enum WorkflowActionKind
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    SentBack = 4,
    Resubmitted = 5,
    Withdrawn = 6,
    Escalated = 7,
    Reassigned = 8,
    StageSkipped = 9,
    Completed = 10,
    AutoApproved = 11,
}

/// <summary>One line of a request's history, as the people involved would describe it.</summary>
public sealed class WorkflowAction : TenantAggregateRoot
{
    private WorkflowAction()
    {
    }

    public Guid InstanceId { get; private set; }

    public Guid? TaskId { get; private set; }

    public WorkflowActionKind Kind { get; private set; }

    public Guid? ActorEmployeeId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string? Comment { get; private set; }

    public DateTime OccurredDate { get; private set; }

    public static WorkflowAction Record(
        Guid instanceId,
        WorkflowActionKind kind,
        Guid actorUserId,
        Guid? actorEmployeeId,
        DateTime now,
        Guid? taskId = null,
        string? comment = null) => new()
        {
            InstanceId = instanceId,
            TaskId = taskId,
            Kind = kind,
            ActorUserId = actorUserId,
            ActorEmployeeId = actorEmployeeId,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 1000),
            OccurredDate = now,
        };
}

/// <summary>
/// "While I am away, my approvals go to her." Followed one hop only: a delegate's own delegation
/// does not pass the task on again, so a loop of absences cannot bounce a task forever.
/// </summary>
public sealed class Delegation : TenantAggregateRoot
{
    private Delegation()
    {
    }

    public Guid DelegatorEmployeeId { get; private set; }

    public Guid DelegateEmployeeId { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    /// <summary>Null covers every kind of request.</summary>
    public string? SubjectType { get; private set; }

    public string? Reason { get; private set; }

    public static Delegation Create(
        Guid delegatorEmployeeId,
        Guid delegateEmployeeId,
        DateOnly startsOn,
        DateOnly endsOn,
        string? subjectType,
        string? reason)
    {
        if (delegatorEmployeeId == delegateEmployeeId)
        {
            throw new DomainRuleException("DELEGATION_SELF", "Approvals cannot be delegated to oneself.");
        }

        if (endsOn < startsOn)
        {
            throw new DomainRuleException("DELEGATION_DATES", "A delegation cannot end before it starts.");
        }

        if (endsOn.DayNumber - startsOn.DayNumber > 366)
        {
            throw new DomainRuleException("DELEGATION_TOO_LONG", "A delegation lasts at most a year.");
        }

        return new Delegation
        {
            DelegatorEmployeeId = Guard.NotEmpty(delegatorEmployeeId),
            DelegateEmployeeId = Guard.NotEmpty(delegateEmployeeId),
            StartsOn = startsOn,
            EndsOn = endsOn,
            SubjectType = string.IsNullOrWhiteSpace(subjectType) ? null : Guard.MaxLength(subjectType.Trim().ToLowerInvariant(), 64),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300),
        };
    }

    public bool Covers(string subjectType, DateOnly day)
        => IsActive && day >= StartsOn && day <= EndsOn
            && (SubjectType is null || string.Equals(SubjectType, subjectType, StringComparison.OrdinalIgnoreCase));

    public void Revoke() => IsActive = false;
}
