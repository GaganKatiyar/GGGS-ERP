using Gggs.Workflow.Domain.Workflows;

namespace Gggs.Workflow.Application.Workflows;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    void Add(WorkflowDefinition definition);

    Task<WorkflowDefinitionVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionVersion?> GetPublishedVersionAsync(Guid definitionId, CancellationToken cancellationToken = default);

    Task<int> GetNextVersionNumberAsync(Guid definitionId, CancellationToken cancellationToken = default);

    void Add(WorkflowDefinitionVersion version);
}

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The one running instance for this entity, if any - a source service is expected
    /// to check this itself before calling <c>StartWorkflow</c> again, but the engine checks too:
    /// architecture.md §13's model assumes one workflow per entity at a time.</summary>
    Task<WorkflowInstance?> GetRunningForEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    void Add(WorkflowInstance instance);

    void Add(WorkflowActionLog entry);
}

public interface IWorkflowTaskRepository
{
    Task<WorkflowTask?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTask>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTask>> GetPendingForStepAsync(Guid instanceId, string stepKey, CancellationToken cancellationToken = default);

    void Add(WorkflowTask task);
}

public interface IDelegationRepository
{
    /// <summary>Every delegation currently covering <paramref name="fromUserId"/> as of
    /// <paramref name="onDate"/> - a user can have more than one active delegation target in
    /// principle, though the admin UI in v1 only ever creates one at a time.</summary>
    Task<IReadOnlyList<Delegation>> GetActiveForUserAsync(Guid fromUserId, DateOnly onDate, CancellationToken cancellationToken = default);

    void Add(Delegation delegation);
}
