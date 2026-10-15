using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record TaskListQuery
{
    public TaskType? Type { get; init; }

    public Guid? ColumnId { get; init; }

    public ColumnCategory? Category { get; init; }

    public Guid? AssigneeEmployeeId { get; init; }

    public Guid? SprintId { get; init; }

    /// <summary>Only tasks in no sprint: the product backlog.</summary>
    public bool? Backlog { get; init; }

    public Guid? MilestoneId { get; init; }

    public Guid? PhaseId { get; init; }

    public Guid? ParentTaskId { get; init; }

    public Guid? RequirementId { get; init; }

    public string? Search { get; init; }

    public bool? IncludeDone { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? 100, Sort = Sort, Search = Search };
}

public sealed record PortfolioTaskQuery
{
    public bool? AssignedToMe { get; init; }

    public Guid? ProjectId { get; init; }

    /// <summary>Open (default) or done tasks.</summary>
    public bool? Done { get; init; }

    public bool? Overdue { get; init; }

    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Search = Search };
}

public sealed record TaskItem(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    int Number,
    string Code,
    TaskType Type,
    Guid? ParentTaskId,
    string? ParentCode,
    Guid? PhaseId,
    Guid? MilestoneId,
    Guid? SprintId,
    Guid? RequirementId,
    string Title,
    string? Description,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    Guid? ReporterEmployeeId,
    string? ReporterName,
    WorkPriority Priority,
    Guid ColumnId,
    string? ColumnName,
    ColumnCategory Category,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    int? StoryPoints,
    int Progress,
    bool Billable,
    double Rank,
    DateTime? CompletedDate,
    bool IsOverdue,
    int ChildCount,
    int OpenPredecessors);

public sealed record TaskBody(
    TaskType Type,
    Guid? ParentTaskId,
    Guid? PhaseId,
    Guid? MilestoneId,
    Guid? SprintId,
    Guid? RequirementId,
    Guid? ColumnId,
    string Title,
    string? Description,
    Guid? AssigneeEmployeeId,
    WorkPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    int? StoryPoints,
    int? Progress,
    bool? Billable);

/// <summary>Where a task goes on the board: the column and its position in it (0 = top).</summary>
public sealed record MoveBody(Guid ColumnId, int? Index);

public sealed record BoardView(IReadOnlyList<ColumnItem> Columns, IReadOnlyList<TaskItem> Tasks);

public sealed record DependencyItem(
    Guid Id,
    Guid PredecessorTaskId,
    string? PredecessorCode,
    string? PredecessorTitle,
    bool PredecessorDone,
    Guid SuccessorTaskId,
    string? SuccessorCode,
    string? SuccessorTitle,
    DependencyType Type,
    int LagDays);

public sealed record DependencyBody(Guid PredecessorTaskId, DependencyType Type, int? LagDays);

public sealed record RequirementItem(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    RequirementType Type,
    RequirementPriority Priority,
    string? Source,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    RequirementStatus Status,
    string? AcceptanceCriteria,
    int TaskCount,
    int DoneTaskCount);

public sealed record RequirementBody(
    string Title,
    string? Description,
    RequirementType Type,
    RequirementPriority Priority,
    string? Source,
    Guid? OwnerEmployeeId,
    RequirementStatus? Status,
    string? AcceptanceCriteria);

/// <summary>Tasks, the board, dependencies and requirements (spec §44-45, §47, §49).</summary>
internal sealed class TaskService(
    AppDbContext dbContext,
    ProjectAccess access,
    ProjectService projects,
    INotifier notifier,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private static readonly Dictionary<string, Expression<Func<ProjectTask, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = task => task.Number,
        ["title"] = task => task.Title,
        ["dueDate"] = task => task.DueDate,
        ["priority"] = task => task.Priority,
        ["rank"] = task => task.Rank,
    };

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Tasks --------------------------------------------------------------------------------

    public async Task<PagedResult<TaskItem>> ListAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        PageRequest page = query.ToPageRequest();
        IQueryable<ProjectTask> rows = dbContext.ProjectTasks.AsNoTracking().Where(task => task.ProjectId == projectId);

        if (query.Type is { } type)
        {
            rows = rows.Where(task => task.Type == type);
        }

        if (query.ColumnId is { } columnId)
        {
            rows = rows.Where(task => task.ColumnId == columnId);
        }

        if (query.Category is { } category)
        {
            List<Guid> columns = await dbContext.BoardColumns.AsNoTracking().Where(column => column.ProjectId == projectId && column.Category == category).Select(column => column.Id).ToListAsync(cancellationToken);
            rows = rows.Where(task => columns.Contains(task.ColumnId));
        }
        else if (query.IncludeDone == false)
        {
            rows = rows.Where(task => task.CompletedDate == null);
        }

        if (query.AssigneeEmployeeId is { } assignee)
        {
            rows = rows.Where(task => task.AssigneeEmployeeId == assignee);
        }

        if (query.SprintId is { } sprintId)
        {
            rows = rows.Where(task => task.SprintId == sprintId);
        }
        else if (query.Backlog == true)
        {
            rows = rows.Where(task => task.SprintId == null && task.CompletedDate == null);
        }

        if (query.MilestoneId is { } milestoneId)
        {
            rows = rows.Where(task => task.MilestoneId == milestoneId);
        }

        if (query.PhaseId is { } phaseId)
        {
            rows = rows.Where(task => task.PhaseId == phaseId);
        }

        if (query.ParentTaskId is { } parentId)
        {
            rows = rows.Where(task => task.ParentTaskId == parentId);
        }

        if (query.RequirementId is { } requirementId)
        {
            rows = rows.Where(task => task.RequirementId == requirementId);
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(task => task.Title.Contains(term) || task.Code.Contains(term));
        }

        PagedResult<ProjectTask> result = await rows.ApplySort(page.Sort, Sortable, "number").ToPagedResultAsync(page, task => task, cancellationToken);
        return PagedResult.From(await ToItemsAsync(result.Items, cancellationToken), page, result.TotalCount);
    }

    public async Task<TaskItem> GetAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        ProjectTask task = await FindAsync(projectId, taskId, track: false, cancellationToken);
        return (await ToItemsAsync([task], cancellationToken))[0];
    }

    public async Task<TaskItem> CreateAsync(Guid projectId, TaskBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        Domain.Project project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);

        TaskDetails details = await ValidateAsync(projectId, body, existing: null, cancellationToken);
        BoardColumn column = await ColumnForNewTaskAsync(projectId, body, cancellationToken);
        double rank = await NextRankAsync(column.Id, cancellationToken);

        var task = ProjectTask.Create(projectId, project.NextTaskNumber(), project.Code, details, column, rank, access.Me, Now);
        task.PlanInto(body.SprintId);
        dbContext.ProjectTasks.Add(task);
        await NotifyAssigneeAsync(project, task, previousAssignee: null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([task], cancellationToken))[0];
    }

    public async Task<TaskItem> UpdateAsync(Guid projectId, Guid taskId, TaskBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        ProjectTask task = await FindAsync(projectId, taskId, track: true, cancellationToken);
        Guid? previousAssignee = task.AssigneeEmployeeId;

        TaskDetails details = await ValidateAsync(projectId, body, task, cancellationToken);
        task.Update(details);
        task.PlanInto(body.SprintId);

        if (body.ColumnId is { } columnId && columnId != task.ColumnId)
        {
            BoardColumn column = await ColumnAsync(projectId, columnId, cancellationToken);
            task.MoveTo(column, await NextRankAsync(column.Id, cancellationToken), Now);
        }

        Domain.Project project = await dbContext.Projects.AsNoTracking().FirstAsync(row => row.Id == projectId, cancellationToken);
        await NotifyAssigneeAsync(project, task, previousAssignee, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([task], cancellationToken))[0];
    }

    /// <summary>Drops a task into a column at a position, as a drag on the board does.</summary>
    public async Task<TaskItem> MoveAsync(Guid projectId, Guid taskId, MoveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        ProjectTask task = await FindAsync(projectId, taskId, track: true, cancellationToken);
        BoardColumn column = await ColumnAsync(projectId, body.ColumnId, cancellationToken);

        List<double> ranks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ColumnId == column.Id && row.Id != taskId)
            .OrderBy(row => row.Rank)
            .Select(row => row.Rank)
            .ToListAsync(cancellationToken);

        int index = Math.Clamp(body.Index ?? ranks.Count, 0, ranks.Count);
        double rank = (index, ranks.Count) switch
        {
            (_, 0) => 1000,
            (0, _) => ranks[0] - 1000,
            var (at, count) when at >= count => ranks[^1] + 1000,
            var (at, _) => (ranks[at - 1] + ranks[at]) / 2,
        };

        if (index > 0 && index < ranks.Count && ranks[index] - ranks[index - 1] < 1e-6)
        {
            // Ranks have converged after many moves: spread the column out again.
            List<ProjectTask> crowded = await dbContext.ProjectTasks.Where(row => row.ColumnId == column.Id && row.Id != taskId).OrderBy(row => row.Rank).ToListAsync(cancellationToken);
            for (int position = 0; position < crowded.Count; position++)
            {
                crowded[position].MoveTo(column, (position + (position >= index ? 1 : 0)) * 1000.0, Now);
            }

            rank = index * 1000.0;
        }

        task.MoveTo(column, rank, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([task], cancellationToken))[0];
    }

    public async Task DeleteAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        ProjectTask task = await FindAsync(projectId, taskId, track: true, cancellationToken);

        if (await dbContext.ProjectTasks.AnyAsync(row => row.ParentTaskId == taskId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_HAS_CHILDREN", $"{task.Code} has tasks under it; delete or move them first.");
        }

        List<TaskDependency> links = await dbContext.TaskDependencies
            .Where(row => row.PredecessorTaskId == taskId || row.SuccessorTaskId == taskId)
            .ToListAsync(cancellationToken);
        dbContext.TaskDependencies.RemoveRange(links);
        dbContext.ProjectTasks.Remove(task);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BoardView> BoardAsync(Guid projectId, Guid? sprintId, Guid? assigneeEmployeeId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        IReadOnlyList<ColumnItem> columns = await projects.ColumnsAsync(projectId, cancellationToken);

        // Epics are containers, not cards: they stay off the board.
        IQueryable<ProjectTask> rows = dbContext.ProjectTasks.AsNoTracking().Where(task => task.ProjectId == projectId && task.Type != TaskType.Epic);
        if (sprintId is { } sprint)
        {
            rows = rows.Where(task => task.SprintId == sprint);
        }

        if (assigneeEmployeeId is { } assignee)
        {
            rows = rows.Where(task => task.AssigneeEmployeeId == assignee);
        }

        List<ProjectTask> tasks = await rows.OrderBy(task => task.Rank).Take(1000).ToListAsync(cancellationToken);
        return new BoardView(columns, await ToItemsAsync(tasks, cancellationToken));
    }

    public async Task<PagedResult<TaskItem>> PortfolioAsync(PortfolioTaskQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.TaskView, cancellationToken);
        IQueryable<ProjectTask> rows = dbContext.ProjectTasks.AsNoTracking().Where(task => task.Type != TaskType.Epic);

        if (visible is not null)
        {
            rows = rows.Where(task => visible.Contains(task.ProjectId));
        }

        if (query.AssignedToMe != false)
        {
            Guid me = access.RequireEmployee();
            rows = rows.Where(task => task.AssigneeEmployeeId == me);
        }

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(task => task.ProjectId == projectId);
        }

        rows = query.Done == true ? rows.Where(task => task.CompletedDate != null) : rows.Where(task => task.CompletedDate == null);

        if (query.Overdue == true)
        {
            DateOnly today = await clock.TodayAsync(cancellationToken);
            rows = rows.Where(task => task.DueDate != null && task.DueDate < today);
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(task => task.Title.Contains(term) || task.Code.Contains(term));
        }

        PagedResult<ProjectTask> result = await rows
            .OrderBy(task => task.DueDate == null)
            .ThenBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority)
            .ToPagedResultAsync(page, task => task, cancellationToken);
        return PagedResult.From(await ToItemsAsync(result.Items, cancellationToken, withProject: true), page, result.TotalCount);
    }

    // ----- Dependencies -------------------------------------------------------------------------

    public async Task<IReadOnlyList<DependencyItem>> DependenciesAsync(Guid projectId, Guid? taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        IQueryable<TaskDependency> rows = dbContext.TaskDependencies.AsNoTracking().Where(row => row.ProjectId == projectId);
        if (taskId is { } id)
        {
            rows = rows.Where(row => row.PredecessorTaskId == id || row.SuccessorTaskId == id);
        }

        List<TaskDependency> dependencies = await rows.ToListAsync(cancellationToken);
        List<Guid> taskIds = [.. dependencies.SelectMany(row => new[] { row.PredecessorTaskId, row.SuccessorTaskId }).Distinct()];
        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Select(task => new { task.Id, task.Code, task.Title, Done = task.CompletedDate != null })
            .ToDictionaryAsync(task => task.Id, cancellationToken);

        return [.. dependencies.Select(row => new DependencyItem(
            row.Id, row.PredecessorTaskId, tasks.GetValueOrDefault(row.PredecessorTaskId)?.Code, tasks.GetValueOrDefault(row.PredecessorTaskId)?.Title,
            tasks.GetValueOrDefault(row.PredecessorTaskId)?.Done ?? false, row.SuccessorTaskId, tasks.GetValueOrDefault(row.SuccessorTaskId)?.Code,
            tasks.GetValueOrDefault(row.SuccessorTaskId)?.Title, row.Type, row.LagDays))];
    }

    /// <summary>Makes <paramref name="taskId"/> wait on another task, refusing duplicates and loops.</summary>
    public async Task<IReadOnlyList<DependencyItem>> AddDependencyAsync(Guid projectId, Guid taskId, DependencyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        await FindAsync(projectId, taskId, track: false, cancellationToken);
        await FindAsync(projectId, body.PredecessorTaskId, track: false, cancellationToken);

        List<(Guid Predecessor, Guid Successor)> edges = [.. (await dbContext.TaskDependencies.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .Select(row => new { row.PredecessorTaskId, row.SuccessorTaskId })
                .ToListAsync(cancellationToken))
            .Select(row => (row.PredecessorTaskId, row.SuccessorTaskId))];

        if (edges.Contains((body.PredecessorTaskId, taskId)))
        {
            throw new DomainRuleException("PROJECT_DEPENDENCY_EXISTS", "That dependency is already recorded.");
        }

        if (DependencyGraph.WouldCreateCycle(edges, body.PredecessorTaskId, taskId))
        {
            throw new DomainRuleException("PROJECT_DEPENDENCY_CYCLE", "That would make the tasks wait on each other in a loop.");
        }

        dbContext.TaskDependencies.Add(TaskDependency.Create(projectId, body.PredecessorTaskId, taskId, body.Type, body.LagDays ?? 0));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await DependenciesAsync(projectId, taskId, cancellationToken);
    }

    public async Task RemoveDependencyAsync(Guid projectId, Guid dependencyId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        TaskDependency dependency = await dbContext.TaskDependencies.FirstOrDefaultAsync(row => row.Id == dependencyId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskDependency), dependencyId);
        dbContext.TaskDependencies.Remove(dependency);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Requirements -------------------------------------------------------------------------

    public async Task<IReadOnlyList<RequirementItem>> RequirementsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        List<Requirement> requirements = await dbContext.Requirements.AsNoTracking().Where(row => row.ProjectId == projectId).OrderBy(row => row.Number).ToListAsync(cancellationToken);
        return await ToRequirementItemsAsync(projectId, requirements, cancellationToken);
    }

    public async Task<RequirementItem> SaveRequirementAsync(Guid projectId, Guid? requirementId, RequirementBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        await projects.EnsureEmployeesAsync([body.OwnerEmployeeId], cancellationToken);

        Requirement requirement;
        if (requirementId is { } id)
        {
            requirement = await dbContext.Requirements.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Requirement), id);
            requirement.Update(ToDetails(body, body.Status ?? requirement.Status));
        }
        else
        {
            Domain.Project project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);
            requirement = Requirement.Create(projectId, project.NextRequirementNumber(), ToDetails(body, body.Status ?? RequirementStatus.Draft));
            dbContext.Requirements.Add(requirement);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToRequirementItemsAsync(projectId, [requirement], cancellationToken))[0];
    }

    public async Task DeleteRequirementAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        Requirement requirement = await dbContext.Requirements.FirstOrDefaultAsync(row => row.Id == requirementId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Requirement), requirementId);

        if (await dbContext.ProjectTasks.AnyAsync(task => task.RequirementId == requirementId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_REQUIREMENT_IN_USE", $"Tasks trace to {requirement.Code}; unlink them first, or mark it rejected or deferred.");
        }

        dbContext.Requirements.Remove(requirement);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<TaskDetails> ValidateAsync(Guid projectId, TaskBody body, ProjectTask? existing, CancellationToken cancellationToken)
    {
        TaskType? parentType = null;
        if (body.ParentTaskId is { } parentId)
        {
            ProjectTask parent = await dbContext.ProjectTasks.AsNoTracking().FirstOrDefaultAsync(task => task.Id == parentId && task.ProjectId == projectId, cancellationToken)
                ?? throw new DomainRuleException("PROJECT_TASK_PARENT", "The parent task is not in this project.");
            parentType = parent.Type;
        }

        TaskHierarchy.Ensure(body.Type, parentType);

        if (existing is not null && existing.Type != body.Type)
        {
            List<TaskType> childTypes = await dbContext.ProjectTasks.AsNoTracking().Where(task => task.ParentTaskId == existing.Id).Select(task => task.Type).Distinct().ToListAsync(cancellationToken);
            foreach (TaskType child in childTypes)
            {
                TaskHierarchy.Ensure(child, body.Type);
            }
        }

        if (body.PhaseId is { } phaseId && !await dbContext.ProjectPhases.AnyAsync(phase => phase.Id == phaseId && phase.ProjectId == projectId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_PHASE", "The phase is not in this project.");
        }

        if (body.MilestoneId is { } milestoneId && !await dbContext.ProjectMilestones.AnyAsync(milestone => milestone.Id == milestoneId && milestone.ProjectId == projectId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_MILESTONE", "The milestone is not in this project.");
        }

        if (body.RequirementId is { } requirementId && !await dbContext.Requirements.AnyAsync(requirement => requirement.Id == requirementId && requirement.ProjectId == projectId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_REQUIREMENT", "The requirement is not in this project.");
        }

        if (body.SprintId is { } sprintId && body.SprintId != existing?.SprintId
            && !await dbContext.Sprints.AnyAsync(sprint => sprint.Id == sprintId && sprint.ProjectId == projectId && (sprint.Status == SprintStatus.Planned || sprint.Status == SprintStatus.Active), cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_SPRINT", "Tasks can only be planned into an open sprint of this project.");
        }

        if (body.AssigneeEmployeeId is { } assignee && assignee != existing?.AssigneeEmployeeId && !await IsOnTeamAsync(projectId, assignee, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TASK_ASSIGNEE", "Assign tasks to someone on the project team.");
        }

        return new TaskDetails(
            body.Type, body.ParentTaskId, body.PhaseId, body.MilestoneId, body.RequirementId, body.Title, body.Description, body.AssigneeEmployeeId,
            body.Priority, body.StartDate, body.DueDate, body.EstimatedHours, body.ActualHours, body.StoryPoints, body.Progress ?? existing?.Progress ?? 0,
            body.Billable ?? existing?.Billable ?? true);
    }

    /// <summary>On the team now, or one of the project's managers.</summary>
    private async Task<bool> IsOnTeamAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken)
        => await dbContext.ProjectTeamMembers.AnyAsync(member => member.ProjectId == projectId && member.EmployeeId == employeeId && member.Status == TeamMemberStatus.Active, cancellationToken)
            || await dbContext.Projects.AnyAsync(
                project => project.Id == projectId
                    && (project.ProjectManagerEmployeeId == employeeId || project.DeliveryManagerEmployeeId == employeeId || project.AccountManagerEmployeeId == employeeId),
                cancellationToken);

    private async Task<BoardColumn> ColumnForNewTaskAsync(Guid projectId, TaskBody body, CancellationToken cancellationToken)
    {
        if (body.ColumnId is { } columnId)
        {
            return await ColumnAsync(projectId, columnId, cancellationToken);
        }

        // Work planned into a sprint is ready to start; anything else waits in the backlog.
        ColumnCategory wanted = body.SprintId is null ? ColumnCategory.Backlog : ColumnCategory.Todo;
        List<BoardColumn> columns = await dbContext.BoardColumns.Where(column => column.ProjectId == projectId).OrderBy(column => column.SortOrder).ToListAsync(cancellationToken);
        return columns.FirstOrDefault(column => column.Category == wanted)
            ?? columns.FirstOrDefault(column => column.Category != ColumnCategory.Done)
            ?? throw new DomainRuleException("PROJECT_BOARD_SHAPE", "The board has no column for new work.");
    }

    private async Task<BoardColumn> ColumnAsync(Guid projectId, Guid columnId, CancellationToken cancellationToken)
        => await dbContext.BoardColumns.FirstOrDefaultAsync(column => column.Id == columnId && column.ProjectId == projectId, cancellationToken)
            ?? throw new DomainRuleException("PROJECT_COLUMN_UNKNOWN", "That column is not on this project's board.");

    private async Task<double> NextRankAsync(Guid columnId, CancellationToken cancellationToken)
        => (await dbContext.ProjectTasks.Where(task => task.ColumnId == columnId).MaxAsync(task => (double?)task.Rank, cancellationToken) ?? 0) + 1000;

    private async Task<ProjectTask> FindAsync(Guid projectId, Guid taskId, bool track, CancellationToken cancellationToken)
    {
        IQueryable<ProjectTask> source = track ? dbContext.ProjectTasks : dbContext.ProjectTasks.AsNoTracking();
        return await source.FirstOrDefaultAsync(task => task.Id == taskId && task.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException("Task", taskId);
    }

    private async Task NotifyAssigneeAsync(Domain.Project project, ProjectTask task, Guid? previousAssignee, CancellationToken cancellationToken)
    {
        if (task.AssigneeEmployeeId is { } assignee && assignee != previousAssignee && assignee != access.Me)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ProjectTaskAssigned,
                    [new NotificationRecipient(assignee)],
                    new Dictionary<string, string> { ["taskCode"] = task.Code, ["taskTitle"] = task.Title, ["projectName"] = project.Name },
                    $"/projects/{project.Id}?tab=tasks&task={task.Id}"),
                cancellationToken);
        }
    }

    private static RequirementDetails ToDetails(RequirementBody body, RequirementStatus status)
        => new(body.Title, body.Description, body.Type, body.Priority, body.Source, body.OwnerEmployeeId, status, body.AcceptanceCriteria);

    private async Task<List<RequirementItem>> ToRequirementItemsAsync(Guid projectId, IReadOnlyList<Requirement> requirements, CancellationToken cancellationToken)
    {
        var counts = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId && task.RequirementId != null)
            .GroupBy(task => task.RequirementId!.Value)
            .Select(group => new { group.Key, Total = group.Count(), Done = group.Count(task => task.CompletedDate != null) })
            .ToDictionaryAsync(row => row.Key, cancellationToken);
        Dictionary<Guid, string> owners = await projects.NamesAsync(requirements.Select(row => row.OwnerEmployeeId), cancellationToken);

        return [.. requirements.Select(row => new RequirementItem(
            row.Id, row.Code, row.Title, row.Description, row.Type, row.Priority, row.Source, row.OwnerEmployeeId,
            row.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null, row.Status, row.AcceptanceCriteria,
            counts.GetValueOrDefault(row.Id)?.Total ?? 0, counts.GetValueOrDefault(row.Id)?.Done ?? 0))];
    }

    internal async Task<List<TaskItem>> ToItemsAsync(IReadOnlyList<ProjectTask> tasks, CancellationToken cancellationToken, bool withProject = false)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> ids = [.. tasks.Select(task => task.Id)];
        List<Guid> columnIds = [.. tasks.Select(task => task.ColumnId).Distinct()];
        Dictionary<Guid, BoardColumn> columns = await dbContext.BoardColumns.AsNoTracking().Where(column => columnIds.Contains(column.Id)).ToDictionaryAsync(column => column.Id, cancellationToken);
        List<Guid> parentIds = [.. tasks.Select(task => task.ParentTaskId).OfType<Guid>().Distinct()];
        Dictionary<Guid, string> parents = parentIds.Count == 0
            ? []
            : await dbContext.ProjectTasks.AsNoTracking().Where(task => parentIds.Contains(task.Id)).ToDictionaryAsync(task => task.Id, task => task.Code, cancellationToken);
        Dictionary<Guid, int> children = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ParentTaskId != null && ids.Contains(task.ParentTaskId.Value))
            .GroupBy(task => task.ParentTaskId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        Dictionary<Guid, int> blocked = (await dbContext.TaskDependencies.AsNoTracking()
                .Where(row => ids.Contains(row.SuccessorTaskId))
                .Join(dbContext.ProjectTasks.AsNoTracking(), row => row.PredecessorTaskId, task => task.Id, (row, task) => new { row.SuccessorTaskId, Done = task.CompletedDate != null })
                .Where(row => !row.Done)
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.SuccessorTaskId)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<Guid, string> people = await projects.NamesAsync(tasks.SelectMany(task => new[] { task.AssigneeEmployeeId, task.ReporterEmployeeId }), cancellationToken);

        Dictionary<Guid, (string Code, string Name)> projectNames = [];
        if (withProject)
        {
            List<Guid> projectIds = [.. tasks.Select(task => task.ProjectId).Distinct()];
            projectNames = await dbContext.Projects.AsNoTracking().Where(project => projectIds.Contains(project.Id))
                .ToDictionaryAsync(project => project.Id, project => (project.Code, project.Name), cancellationToken);
        }

        return [.. tasks.Select(task =>
        {
            BoardColumn? column = columns.GetValueOrDefault(task.ColumnId);
            (string Code, string Name)? project = projectNames.TryGetValue(task.ProjectId, out (string Code, string Name) found) ? found : null;
            return new TaskItem(
                task.Id, task.ProjectId, project?.Code, project?.Name, task.Number, task.Code, task.Type, task.ParentTaskId,
                task.ParentTaskId is { } parent ? parents.GetValueOrDefault(parent) : null, task.PhaseId, task.MilestoneId, task.SprintId, task.RequirementId,
                task.Title, task.Description, task.AssigneeEmployeeId, task.AssigneeEmployeeId is { } assignee ? people.GetValueOrDefault(assignee) : null,
                task.ReporterEmployeeId, task.ReporterEmployeeId is { } reporter ? people.GetValueOrDefault(reporter) : null, task.Priority,
                task.ColumnId, column?.Name, column?.Category ?? ColumnCategory.Backlog, task.StartDate, task.DueDate, task.EstimatedHours,
                task.ActualHours, task.StoryPoints, task.Progress, task.Billable, task.Rank, task.CompletedDate, task.IsOverdue(today),
                children.GetValueOrDefault(task.Id), blocked.GetValueOrDefault(task.Id));
        })];
    }
}
