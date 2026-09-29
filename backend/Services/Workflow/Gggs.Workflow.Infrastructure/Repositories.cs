using Gggs.Workflow.Application.Snapshots;
using Gggs.Workflow.Application.Workflows;
using Gggs.Workflow.Domain.Snapshots;
using Gggs.Workflow.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Workflow.Infrastructure;

public sealed class WorkflowDefinitionRepository(WorkflowDbContext dbContext) : IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Definitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WorkflowDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Normalized outside the query, not inline in the lambda: WorkflowDefinition.Create
        // already stores Code uppercased, so this only needs a plain string == once the caller's
        // input matches that same normalization - matching the convention CoreHr's own
        // *Repository classes use for the same reason (OrganizationRepository, EmployeeRepository).
        string normalizedCode = code.Trim().ToUpperInvariant();
        return await dbContext.Definitions.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);
    }

    public void Add(WorkflowDefinition definition) => dbContext.Definitions.Add(definition);

    public async Task<WorkflowDefinitionVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.DefinitionVersions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WorkflowDefinitionVersion?> GetPublishedVersionAsync(Guid definitionId, CancellationToken cancellationToken = default)
        => await dbContext.DefinitionVersions.FirstOrDefaultAsync(
            x => x.DefinitionId == definitionId && x.Status == WorkflowVersionStatus.Published,
            cancellationToken);

    public async Task<int> GetNextVersionNumberAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        int highest = await dbContext.DefinitionVersions
            .Where(x => x.DefinitionId == definitionId)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;

        return highest + 1;
    }

    public void Add(WorkflowDefinitionVersion version) => dbContext.DefinitionVersions.Add(version);
}

public sealed class WorkflowInstanceRepository(WorkflowDbContext dbContext) : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Instances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WorkflowInstance?> GetRunningForEntityAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        string normalizedEntityType = entityType.Trim().ToUpperInvariant();
        return await dbContext.Instances.FirstOrDefaultAsync(
            x => x.EntityType == normalizedEntityType && x.EntityId == entityId && x.Status == WorkflowInstanceStatus.Running,
            cancellationToken);
    }

    public void Add(WorkflowInstance instance) => dbContext.Instances.Add(instance);

    public void Add(WorkflowActionLog entry) => dbContext.ActionLog.Add(entry);
}

public sealed class WorkflowTaskRepository(WorkflowDbContext dbContext) : IWorkflowTaskRepository
{
    public async Task<WorkflowTask?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Tasks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkflowTask>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => await dbContext.Tasks.Where(x => x.InstanceId == instanceId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowTask>> GetPendingForStepAsync(
        Guid instanceId, string stepKey, CancellationToken cancellationToken = default)
        => await dbContext.Tasks
            .Where(x => x.InstanceId == instanceId && x.StepKey == stepKey && x.Status == WorkflowTaskStatus.Pending)
            .ToListAsync(cancellationToken);

    public void Add(WorkflowTask task) => dbContext.Tasks.Add(task);
}

public sealed class DelegationRepository(WorkflowDbContext dbContext) : IDelegationRepository
{
    public async Task<IReadOnlyList<Delegation>> GetActiveForUserAsync(
        Guid fromUserId, DateOnly onDate, CancellationToken cancellationToken = default)
        => await dbContext.Delegations
            .Where(x => x.FromUserId == fromUserId && x.IsActive && x.StartDate <= onDate && x.EndDate >= onDate)
            .ToListAsync(cancellationToken);

    public void Add(Delegation delegation) => dbContext.Delegations.Add(delegation);
}

public sealed class EmployeeSnapshotRepository(WorkflowDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
