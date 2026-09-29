using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Workflow.Domain.Workflows;

/// <summary>
/// One request working its way through a <see cref="WorkflowDefinitionVersion"/>'s steps - "this
/// leave request, going through approval". Pinned to the version it was created from for its whole
/// life (architecture.md §13.3 step 2): publishing a new definition version never reaches back into
/// an instance already running.
/// </summary>
public sealed class WorkflowInstance : TenantAggregateRoot
{
    private WorkflowInstance()
    {
        EntityType = null!;
        SourceService = null!;
        ContextJson = null!;
    }

    public Guid DefinitionVersionId { get; private set; }

    /// <summary>What is being approved and where it lives - "LEAVE_REQUEST" / the request's own id
    /// in Time's database. Workflow never reads that row; everything a step's condition or an
    /// approver rule needs travels in <see cref="ContextJson"/>.</summary>
    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public string SourceService { get; private set; }

    public Guid InitiatorUserId { get; private set; }

    /// <summary>Whose record this concerns - not always the initiator (an HR admin can submit
    /// leave on someone else's behalf). Used for segregation-of-duties: the subject and the
    /// initiator are both excluded from resolved approver lists for their own instance.</summary>
    public Guid SubjectEmployeeId { get; private set; }

    public WorkflowInstanceStatus Status { get; private set; }

    /// <summary>Null once the instance leaves its first step by any path (approved, rejected,
    /// cancelled) - there is always a "current step" while <see cref="Status"/> is Running.</summary>
    public string? CurrentStepKey { get; private set; }

    /// <summary>The data steps' <c>enterIf</c> conditions and approver rules read - amount, days,
    /// department id, whatever the source service decided mattered when it started this instance.
    /// Never edited after creation.</summary>
    public string ContextJson { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTime StartedDate { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public static WorkflowInstance Start(
        Guid definitionVersionId,
        string entityType,
        Guid entityId,
        string sourceService,
        Guid initiatorUserId,
        Guid subjectEmployeeId,
        string contextJson,
        string? correlationId,
        string firstStepKey,
        DateTime now) => new()
        {
            DefinitionVersionId = Guard.NotEmpty(definitionVersionId),
            EntityType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityType).Trim().ToUpperInvariant(), 50),
            EntityId = Guard.NotEmpty(entityId),
            SourceService = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sourceService).Trim(), 50),
            InitiatorUserId = Guard.NotEmpty(initiatorUserId),
            SubjectEmployeeId = Guard.NotEmpty(subjectEmployeeId),
            Status = WorkflowInstanceStatus.Running,
            CurrentStepKey = Guard.NotNullOrWhiteSpace(firstStepKey),
            ContextJson = Guard.NotNullOrWhiteSpace(contextJson),
            CorrelationId = correlationId,
            StartedDate = now,
            IsActive = true,
        };

    public void AdvanceTo(string nextStepKey)
    {
        Guard.Against(Status != WorkflowInstanceStatus.Running, "WORKFLOW_INSTANCE_NOT_RUNNING", "This instance is not running.");
        CurrentStepKey = Guard.NotNullOrWhiteSpace(nextStepKey);
    }

    public void Complete(WorkflowInstanceStatus outcome, DateTime now)
    {
        Guard.Against(Status != WorkflowInstanceStatus.Running, "WORKFLOW_INSTANCE_NOT_RUNNING", "This instance is not running.");
        Guard.Against(
            outcome == WorkflowInstanceStatus.Running,
            "WORKFLOW_INSTANCE_INVALID_OUTCOME",
            "Completing an instance requires a terminal status.");

        Status = outcome;
        CurrentStepKey = null;
        CompletedDate = now;
    }
}

public enum WorkflowInstanceStatus
{
    Running = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}
