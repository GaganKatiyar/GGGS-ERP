using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Workflow.Domain.Workflows;

/// <summary>
/// A named approval process for one entity type ("LEAVE_REQUEST", "EXPENSE_CLAIM"...), owned by a
/// tenant. The definition itself never carries the step configuration - see
/// <see cref="WorkflowDefinitionVersion"/> - so editing a workflow is publishing a new version, not
/// mutating one a running <see cref="WorkflowInstance"/> is already pinned to.
/// </summary>
public sealed class WorkflowDefinition : TenantAggregateRoot
{
    private WorkflowDefinition()
    {
        Code = null!;
        Name = null!;
        EntityType = null!;
    }

    /// <summary>Stable identifier a source service starts an instance by, e.g. "LEAVE_REQUEST".
    /// Uppercase by convention, matching the architecture doc's own examples.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>What kind of record this approves - informational only in v1 (no cross-service
    /// validation that the entity type is real), but every instance created from this definition
    /// is expected to carry it, and <c>WorkflowInstanceQueries</c> filters on it.</summary>
    public string EntityType { get; private set; }

    /// <summary>Seeded system definitions (none ship yet - every tenant builds its own in v1)
    /// cannot be deleted, matching <c>iam.Role.IsSystem</c>'s own convention.</summary>
    public bool IsSystem { get; private set; }

    public static WorkflowDefinition Create(string code, string name, string entityType) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 50),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
        EntityType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityType).Trim().ToUpperInvariant(), 50),
        IsSystem = false,
        IsActive = true,
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150);
}

/// <summary>
/// One published (or draft) revision of a definition's steps. A running instance is created from,
/// and stays pinned to, exactly one version for its whole lifetime (architecture.md §13.3 step 2) -
/// publishing a new version never affects work already in flight.
/// </summary>
public sealed class WorkflowDefinitionVersion : TenantAggregateRoot
{
    private WorkflowDefinitionVersion()
    {
        DefinitionJson = null!;
    }

    public Guid DefinitionId { get; private set; }

    public int Version { get; private set; }

    public WorkflowVersionStatus Status { get; private set; }

    /// <summary>The step list, serialized - see <c>WorkflowDefinitionModel</c> (Application layer)
    /// for the shape this deserializes into. Kept as a JSON blob here rather than normalized
    /// tables: a definition is edited as a whole document by its own admin screen, never by a
    /// single-step patch, so there is no query that needs step rows to be relational.</summary>
    public string DefinitionJson { get; private set; }

    public DateTime? PublishedDate { get; private set; }

    public static WorkflowDefinitionVersion CreateDraft(Guid definitionId, int version, string definitionJson) => new()
    {
        DefinitionId = Guard.NotEmpty(definitionId),
        Version = version,
        Status = WorkflowVersionStatus.Draft,
        DefinitionJson = Guard.NotNullOrWhiteSpace(definitionJson),
        IsActive = true,
    };

    public void UpdateDraft(string definitionJson)
    {
        Guard.Against(
            Status != WorkflowVersionStatus.Draft,
            "WORKFLOW_VERSION_NOT_DRAFT",
            "Only a draft version can be edited - publish a new version instead.");

        DefinitionJson = Guard.NotNullOrWhiteSpace(definitionJson);
    }

    public void Publish(DateTime now)
    {
        Guard.Against(
            Status != WorkflowVersionStatus.Draft,
            "WORKFLOW_VERSION_NOT_DRAFT",
            "Only a draft version can be published.");

        Status = WorkflowVersionStatus.Published;
        PublishedDate = now;
    }

    public void Retire()
    {
        Guard.Against(
            Status != WorkflowVersionStatus.Published,
            "WORKFLOW_VERSION_NOT_PUBLISHED",
            "Only a published version can be retired.");

        Status = WorkflowVersionStatus.Retired;
    }
}

public enum WorkflowVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}
