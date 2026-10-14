using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Domain;
using Gggs.Modules.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workflow.Application;

public sealed record TaskItem(
    Guid Id,
    Guid InstanceId,
    string SubjectType,
    string SubjectName,
    string Title,
    string? Link,
    string StageName,
    WorkflowTaskStatus Status,
    Guid AssigneeEmployeeId,
    string? AssigneeName,
    string? DelegatedFromName,
    string? RequesterName,
    DateTime SubmittedDate,
    DateTime? DueDate,
    bool IsOverdue,
    DateTime? EscalatedDate,
    DateTime? ActedDate,
    string? Comment);

public sealed record InstanceItem(
    Guid Id,
    string SubjectType,
    string SubjectName,
    Guid SubjectId,
    string Title,
    string? Link,
    WorkflowInstanceStatus Status,
    int Round,
    string? RequesterName,
    string? CurrentStageName,
    DateTime SubmittedDate,
    DateTime? CompletedDate);

public sealed record StageView(int Index, string Name, StageMode Mode, string? Condition, int? SlaHours, string State);

public sealed record ActionItem(WorkflowActionKind Kind, string? ActorName, string? Comment, DateTime OccurredDate);

public sealed record InstanceDetail(
    InstanceItem Request,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<StageView> Stages,
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<ActionItem> History,
    bool CanWithdraw,
    bool CanResubmit);

public sealed record DefinitionItem(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string SubjectType,
    string SubjectName,
    int Version,
    bool IsActive,
    Guid? FallbackApproverEmployeeId,
    string? FallbackApproverName,
    IReadOnlyList<WorkflowStage> Stages);

public sealed record DelegationItem(
    Guid Id,
    Guid DelegatorEmployeeId,
    string? DelegatorName,
    Guid DelegateEmployeeId,
    string? DelegateName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string? SubjectType,
    string? Reason,
    bool IsActive,
    bool IsCurrent);

public sealed record TaskListQuery
{
    /// <summary><c>pending</c> (default) or <c>done</c>.</summary>
    public string? Status { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

public sealed record InstanceListQuery
{
    /// <summary><c>mine</c> (default): requests I raised. <c>all</c>: everyone's, with workflow.instance.view-all.</summary>
    public string? Scope { get; init; }

    public WorkflowInstanceStatus? Status { get; init; }

    public string? SubjectType { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

/// <summary>The inbox, request history and configuration views. Everything here is read-only.</summary>
internal sealed class WorkflowQueries(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    IEnumerable<WorkflowSubject> subjects,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public IReadOnlyList<WorkflowSubject> Subjects() => [.. subjects.OrderBy(subject => subject.Name, StringComparer.CurrentCulture)];

    public async Task<int> PendingCountAsync(CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } me)
        {
            return 0;
        }

        return await dbContext.WorkflowTasks.CountAsync(task => task.AssigneeEmployeeId == me && task.Status == WorkflowTaskStatus.Pending, cancellationToken);
    }

    public async Task<PagedResult<TaskItem>> MyTasksAsync(TaskListQuery query, CancellationToken cancellationToken)
    {
        PageRequest page = query.ToPageRequest();
        if (currentUser.EmployeeId is not { } me)
        {
            return PagedResult.From(Array.Empty<TaskItem>(), page, 0);
        }

        bool done = string.Equals(query.Status, "done", StringComparison.OrdinalIgnoreCase);

        IQueryable<WorkflowTask> tasks = dbContext.WorkflowTasks.AsNoTracking().Where(task => task.AssigneeEmployeeId == me);
        tasks = done
            ? tasks.Where(task => task.Status != WorkflowTaskStatus.Pending).OrderByDescending(task => task.ActedDate)
            : tasks.Where(task => task.Status == WorkflowTaskStatus.Pending).OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate).ThenBy(task => task.CreatedDate);

        long total = await tasks.LongCountAsync(cancellationToken);
        List<WorkflowTask> rows = await tasks.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return PagedResult.From(await ToTaskItemsAsync(rows, cancellationToken), page, total);
    }

    public async Task<PagedResult<InstanceItem>> InstancesAsync(InstanceListQuery query, CancellationToken cancellationToken)
    {
        PageRequest page = query.ToPageRequest();
        IQueryable<WorkflowInstance> instances = dbContext.WorkflowInstances.AsNoTracking();

        if (string.Equals(query.Scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!await SeesAllAsync(cancellationToken))
            {
                throw new ForbiddenException("WORKFLOW_VIEW_ALL", "Viewing everyone's requests needs workflow.instance.view-all for the whole organization.");
            }
        }
        else
        {
            Guid userId = currentUser.UserId;
            Guid? me = currentUser.EmployeeId;
            instances = instances.Where(instance => instance.RequestedByUserId == userId || (me != null && instance.RequesterEmployeeId == me));
        }

        if (query.Status is { } status)
        {
            instances = instances.Where(instance => instance.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            string subjectType = query.SubjectType.Trim().ToLowerInvariant();
            instances = instances.Where(instance => instance.SubjectType == subjectType);
        }

        long total = await instances.LongCountAsync(cancellationToken);
        List<WorkflowInstance> rows = await instances
            .OrderByDescending(instance => instance.SubmittedDate)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, string> names = await NamesAsync(rows.Select(row => row.RequesterEmployeeId), cancellationToken);
        return PagedResult.From(rows.Select(row => ToInstanceItem(row, names)).ToList(), page, total);
    }

    public async Task<InstanceDetail> InstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        WorkflowInstance instance = await dbContext.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(row => row.Id == instanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowInstance), instanceId);

        List<WorkflowTask> tasks = await dbContext.WorkflowTasks.AsNoTracking()
            .Where(task => task.InstanceId == instanceId)
            .OrderBy(task => task.Round).ThenBy(task => task.StageIndex).ThenBy(task => task.CreatedDate)
            .ToListAsync(cancellationToken);

        bool isRequester = instance.RequestedByUserId == currentUser.UserId
            || (currentUser.EmployeeId is { } me && instance.RequesterEmployeeId == me);
        bool isApprover = currentUser.EmployeeId is { } self && tasks.Any(task => task.AssigneeEmployeeId == self);

        if (!isRequester && !isApprover && !await SeesAllAsync(cancellationToken))
        {
            throw new NotFoundException(nameof(WorkflowInstance), instanceId);
        }

        List<WorkflowAction> actions = await dbContext.WorkflowActions.AsNoTracking()
            .Where(action => action.InstanceId == instanceId)
            .OrderBy(action => action.OccurredDate)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, string> names = await NamesAsync(
            actions.Select(action => action.ActorEmployeeId).Append(instance.RequesterEmployeeId),
            cancellationToken);

        IReadOnlyList<WorkflowStage> stages = instance.Stages;
        List<StageView> stageViews = [.. stages.Select((stage, index) => new StageView(
            index,
            stage.Name,
            stage.Mode,
            stage.Condition is { } condition ? $"{condition.Field} {condition.Operator} {condition.Value}" : null,
            stage.SlaHours,
            StageState(instance, tasks, index)))];

        return new InstanceDetail(
            ToInstanceItem(instance, names),
            instance.Fields,
            stageViews,
            await ToTaskItemsAsync(tasks, cancellationToken, instance),
            [.. actions.Select(action => new ActionItem(
                action.Kind,
                action.ActorEmployeeId is { } actor ? names.GetValueOrDefault(actor) : null,
                action.Comment,
                action.OccurredDate))],
            CanWithdraw: isRequester && instance.IsOpen,
            CanResubmit: isRequester && instance.Status == WorkflowInstanceStatus.SentBack);
    }

    public async Task<IReadOnlyList<DefinitionItem>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        List<WorkflowDefinition> definitions = await dbContext.WorkflowDefinitions.AsNoTracking()
            .OrderBy(definition => definition.Name)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, string> names = await NamesAsync(definitions.Select(definition => definition.FallbackApproverEmployeeId), cancellationToken);
        return [.. definitions.Select(definition => ToDefinitionItem(definition, names))];
    }

    public async Task<DefinitionItem> DefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkflowDefinition definition = await dbContext.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinition), id);

        IReadOnlyDictionary<Guid, string> names = await NamesAsync([definition.FallbackApproverEmployeeId], cancellationToken);
        return ToDefinitionItem(definition, names);
    }

    public async Task<IReadOnlyList<DelegationItem>> DelegationsAsync(bool everyone, CancellationToken cancellationToken)
    {
        IQueryable<Delegation> delegations = dbContext.Delegations.AsNoTracking();

        if (!everyone)
        {
            Guid me = currentUser.EmployeeId ?? Guid.Empty;
            delegations = delegations.Where(delegation => delegation.DelegatorEmployeeId == me || delegation.DelegateEmployeeId == me);
        }

        List<Delegation> rows = await delegations.OrderByDescending(delegation => delegation.StartsOn).Take(500).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, string> names = await NamesAsync(
            rows.SelectMany(row => new Guid?[] { row.DelegatorEmployeeId, row.DelegateEmployeeId }),
            cancellationToken);

        DateOnly today = DateOnly.FromDateTime(Now);
        return [.. rows.Select(row => new DelegationItem(
            row.Id,
            row.DelegatorEmployeeId,
            names.GetValueOrDefault(row.DelegatorEmployeeId),
            row.DelegateEmployeeId,
            names.GetValueOrDefault(row.DelegateEmployeeId),
            row.StartsOn,
            row.EndsOn,
            row.SubjectType,
            row.Reason,
            row.IsActive,
            row.IsActive && today >= row.StartsOn && today <= row.EndsOn))];
    }

    /// <summary>Whether the caller holds workflow.instance.view-all for the whole tenant.</summary>
    public async Task<bool> SeesAllAsync(CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);
        return held.ScopeFor(Permissions.Workflow.InstanceViewAll) == DataScope.Tenant;
    }

    private static string StageState(WorkflowInstance instance, List<WorkflowTask> tasks, int index)
    {
        List<WorkflowTask> current = [.. tasks.Where(task => task.Round == instance.Round && task.StageIndex == index)];

        if (instance.CurrentStage == index)
        {
            return "InProgress";
        }

        if (current.Count == 0)
        {
            return instance.Status == WorkflowInstanceStatus.Approved || index < instance.CurrentStage ? "Skipped" : "Waiting";
        }

        if (current.Any(task => task.Status == WorkflowTaskStatus.Rejected))
        {
            return "Rejected";
        }

        if (current.Any(task => task.Status == WorkflowTaskStatus.SentBack))
        {
            return "SentBack";
        }

        return current.Any(task => task.Status == WorkflowTaskStatus.Approved) ? "Approved" : "Closed";
    }

    private async Task<List<TaskItem>> ToTaskItemsAsync(List<WorkflowTask> tasks, CancellationToken cancellationToken, WorkflowInstance? known = null)
    {
        List<Guid> instanceIds = [.. tasks.Select(task => task.InstanceId).Distinct()];
        Dictionary<Guid, WorkflowInstance> instances = known is not null
            ? new() { [known.Id] = known }
            : await dbContext.WorkflowInstances.AsNoTracking()
                .Where(instance => instanceIds.Contains(instance.Id))
                .ToDictionaryAsync(instance => instance.Id, cancellationToken);

        IReadOnlyDictionary<Guid, string> names = await NamesAsync(
            tasks.SelectMany(task => new Guid?[] { task.AssigneeEmployeeId, task.DelegatedFromEmployeeId })
                .Concat(instances.Values.Select(instance => instance.RequesterEmployeeId)),
            cancellationToken);

        DateTime now = Now;
        return [.. tasks.Where(task => instances.ContainsKey(task.InstanceId)).Select(task =>
        {
            WorkflowInstance instance = instances[task.InstanceId];
            return new TaskItem(
                task.Id,
                instance.Id,
                instance.SubjectType,
                SubjectName(instance.SubjectType),
                instance.Title,
                instance.Link,
                task.StageName,
                task.Status,
                task.AssigneeEmployeeId,
                names.GetValueOrDefault(task.AssigneeEmployeeId),
                task.DelegatedFromEmployeeId is { } from ? names.GetValueOrDefault(from) : null,
                instance.RequesterEmployeeId is { } requester ? names.GetValueOrDefault(requester) : null,
                instance.SubmittedDate,
                task.DueDate,
                task.IsPending && task.DueDate is { } due && due < now,
                task.EscalatedDate,
                task.ActedDate,
                task.Comment);
        })];
    }

    private InstanceItem ToInstanceItem(WorkflowInstance instance, IReadOnlyDictionary<Guid, string> names)
    {
        IReadOnlyList<WorkflowStage> stages = instance.Stages;
        return new InstanceItem(
            instance.Id,
            instance.SubjectType,
            SubjectName(instance.SubjectType),
            instance.SubjectId,
            instance.Title,
            instance.Link,
            instance.Status,
            instance.Round,
            instance.RequesterEmployeeId is { } requester ? names.GetValueOrDefault(requester) : null,
            instance.CurrentStage >= 0 && instance.CurrentStage < stages.Count ? stages[instance.CurrentStage].Name : null,
            instance.SubmittedDate,
            instance.CompletedDate);
    }

    private DefinitionItem ToDefinitionItem(WorkflowDefinition definition, IReadOnlyDictionary<Guid, string> names)
        => new(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.Description,
            definition.SubjectType,
            SubjectName(definition.SubjectType),
            definition.Version,
            definition.IsActive,
            definition.FallbackApproverEmployeeId,
            definition.FallbackApproverEmployeeId is { } fallback ? names.GetValueOrDefault(fallback) : null,
            definition.Stages);

    private string SubjectName(string code)
        => subjects.FirstOrDefault(subject => string.Equals(subject.Code, code, StringComparison.Ordinal))?.Name ?? code;

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmployeeSummary> found = await employees.GetManyAsync(ids.Where(id => id is not null).Select(id => id!.Value), cancellationToken);
        return found.ToDictionary(employee => employee.Id, employee => employee.DisplayName);
    }
}
