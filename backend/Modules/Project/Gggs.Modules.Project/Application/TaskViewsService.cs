using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record GanttTask(
    Guid Id,
    string Code,
    string Title,
    TaskType Type,
    Guid? ParentTaskId,
    string? AssigneeName,
    DateOnly? StartDate,
    DateOnly? DueDate,
    DateOnly? BaselineStartDate,
    DateOnly? BaselineDueDate,
    int Progress,
    bool IsDone,
    bool IsOverdue);

public sealed record GanttDependency(Guid Id, Guid PredecessorTaskId, Guid SuccessorTaskId, DependencyType Type, int LagDays);

public sealed record GanttMilestone(Guid Id, string Name, DateOnly PlannedDate, DateOnly? ActualDate, string Status);

public sealed record GanttView(
    DateOnly ProjectStart,
    DateOnly ProjectEnd,
    DateOnly? BaselinedOn,
    DateOnly? BaselineEndDate,
    DateOnly Today,
    IReadOnlyList<GanttTask> Tasks,
    IReadOnlyList<GanttDependency> Dependencies,
    IReadOnlyList<GanttMilestone> Milestones,
    bool Truncated,
    bool CanEdit);

public sealed record WorkloadWeek(DateOnly WeekStart, decimal PlannedHours, decimal CapacityHours);

public sealed record WorkloadRow(
    Guid EmployeeId,
    string? EmployeeName,
    int OpenTasks,
    int Overdue,
    int Unestimated,
    decimal RemainingHours,
    decimal UnscheduledHours,
    IReadOnlyList<WorkloadWeek> Weeks);

public sealed record WorkloadView(IReadOnlyList<DateOnly> Weeks, IReadOnlyList<WorkloadRow> People, int UnassignedTasks);

public sealed record WeekTask(string Code, string Title, string? AssigneeName, DateOnly? DueDate);

public sealed record WeekMilestone(string Name, DateOnly PlannedDate, string Status);

public sealed record WeekFacts(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<WeekTask> Finished,
    IReadOnlyList<WeekTask> Late,
    IReadOnlyList<WeekTask> DueSoon,
    IReadOnlyList<WeekMilestone> MilestonesSoon,
    int OpenTasks);

public sealed record ImportBody(string Csv);

public sealed record ImportPreviewRow(
    int Line,
    string? Key,
    string Title,
    TaskType Type,
    WorkPriority Priority,
    string? AssigneeName,
    string ColumnName,
    string? ParentKey,
    DateOnly? DueDate,
    bool AlreadyImported,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record ImportPreview(IReadOnlyList<ImportPreviewRow> Rows, int WillCreate, int AlreadyImported, int WithErrors);

public sealed record ImportResult(int Created, int AlreadyImported, int WithErrors);

/// <summary>Turns the codes people and assistants use into ids, within one project.</summary>
internal sealed class GovernanceProjectLookup(AppDbContext dbContext)
{
    public async Task<Guid?> TaskByCodeAsync(Guid projectId, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string wanted = code.Trim();
        return await dbContext.ProjectTasks.AsNoTracking().Where(row => row.ProjectId == projectId && row.Code == wanted).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("AI_TASKS", $"There is no task {wanted} on this project.");
    }

    public async Task<Guid?> RequirementByCodeAsync(Guid projectId, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string wanted = code.Trim();
        return await dbContext.Requirements.AsNoTracking().Where(row => row.ProjectId == projectId && row.Code == wanted).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("AI_TASKS", $"There is no requirement {wanted} on this project.");
    }
}

/// <summary>The project seen over time and by person, and tasks brought in from a file.</summary>
internal sealed class TaskViewsService(
    AppDbContext dbContext,
    ProjectAccess access,
    ServiceLevelService serviceLevels,
    TaskActivityLog activity,
    IEmployeeDirectory employees,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private const int MostOnAGantt = 1000;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<GanttView> GanttAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        Domain.Project project = await dbContext.Projects.AsNoTracking().FirstAsync(row => row.Id == projectId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ProjectTask> tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ProjectId == projectId)
            .OrderBy(row => row.Number)
            .Take(MostOnAGantt + 1)
            .ToListAsync(cancellationToken);
        bool truncated = tasks.Count > MostOnAGantt;
        if (truncated)
        {
            tasks.RemoveAt(MostOnAGantt);
        }

        Dictionary<Guid, string> names = (await employees.GetManyAsync(tasks.Select(row => row.AssigneeEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        List<TaskDependency> links = await dbContext.TaskDependencies.AsNoTracking().Where(row => row.ProjectId == projectId).ToListAsync(cancellationToken);
        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking().Where(row => row.ProjectId == projectId).OrderBy(row => row.PlannedDate).ToListAsync(cancellationToken);

        return new GanttView(
            project.StartDate, project.EndDate, project.BaselinedOn, project.BaselineEndDate, today,
            [.. tasks.Select(row => new GanttTask(
                row.Id, row.Code, row.Title, row.Type, row.ParentTaskId, row.AssigneeEmployeeId is { } assignee ? names.GetValueOrDefault(assignee) : null,
                row.StartDate, row.DueDate, row.BaselineStartDate, row.BaselineDueDate, row.Progress, row.IsDone, row.IsOverdue(today)))],
            [.. links.Select(row => new GanttDependency(row.Id, row.PredecessorTaskId, row.SuccessorTaskId, row.Type, row.LagDays))],
            [.. milestones.Select(row => new GanttMilestone(row.Id, row.Name, row.PlannedDate, row.ActualDate, row.Status.ToString()))],
            truncated,
            await access.CanAsync(projectId, Permissions.Project.TaskManage, cancellationToken));
    }

    public async Task<WorkloadView> WorkloadAsync(Guid projectId, int? weeks, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        int count = Math.Clamp(weeks ?? 6, 1, 26);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ProjectTask> open = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.CompletedDate == null && row.Type != TaskType.Epic)
            .ToListAsync(cancellationToken);
        var team = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.Status == TeamMemberStatus.Active)
            .Select(row => new { row.EmployeeId, row.AllocationPercent, row.StartDate, row.EndDate })
            .ToListAsync(cancellationToken);

        // People with work here but no seat still show, with no capacity: that is worth seeing.
        List<Guid> people = [.. team.Select(row => row.EmployeeId).Concat(open.Select(row => row.AssigneeEmployeeId).OfType<Guid>()).Distinct()];
        Dictionary<Guid, string> names = (await employees.GetManyAsync(people, cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        DateOnly first = Workload.MondayOf(today);

        List<WorkloadRow> rows = [.. people
            .Select(person =>
            {
                Workload.Person plan = Workload.For(
                    [.. team.Where(row => row.EmployeeId == person).Select(row => new Workload.Assignment(row.AllocationPercent, row.StartDate, row.EndDate))],
                    [.. open.Where(row => row.AssigneeEmployeeId == person).Select(row => new Workload.OpenTask(row.StartDate, row.DueDate, row.EstimatedHours, row.ActualHours))],
                    today,
                    count);
                return new WorkloadRow(
                    person, names.GetValueOrDefault(person), plan.OpenTasks, plan.Overdue, plan.Unestimated, plan.RemainingHours, plan.UnscheduledHours,
                    [.. plan.Weeks.Select(week => new WorkloadWeek(week.WeekStart, week.PlannedHours, week.CapacityHours))]);
            })
            .OrderBy(row => row.EmployeeName, StringComparer.CurrentCulture)];

        return new WorkloadView([.. Enumerable.Range(0, count).Select(index => first.AddDays(index * 7))], rows, open.Count(row => row.AssigneeEmployeeId is null));
    }

    /// <summary>The last seven days and the next fourteen, for a status report.</summary>
    public async Task<WeekFacts> WeekFactsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateTime weekAgo = Now.AddDays(-7);
        List<ProjectTask> tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.Type != TaskType.Epic && (row.CompletedDate == null || row.CompletedDate >= weekAgo))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(tasks.Select(row => row.AssigneeEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        WeekTask Item(ProjectTask task) => new(task.Code, task.Title, task.AssigneeEmployeeId is { } assignee ? names.GetValueOrDefault(assignee) : null, task.DueDate);
        List<ProjectTask> open = [.. tasks.Where(row => !row.IsDone)];
        DateOnly horizon = today.AddDays(14);
        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.PlannedDate <= horizon && row.PlannedDate >= today.AddDays(-14))
            .OrderBy(row => row.PlannedDate)
            .ToListAsync(cancellationToken);

        return new WeekFacts(
            today.AddDays(-7),
            today,
            [.. tasks.Where(row => row.IsDone).Take(30).Select(Item)],
            [.. open.Where(row => row.IsOverdue(today)).OrderBy(row => row.DueDate).Take(30).Select(Item)],
            [.. open.Where(row => row.DueDate >= today && row.DueDate <= horizon).OrderBy(row => row.DueDate).Take(30).Select(Item)],
            [.. milestones.Select(row => new WeekMilestone(row.Name, row.PlannedDate, row.Status.ToString()))],
            open.Count);
    }

    /// <summary>What in the project's data needs attention, by the rules in <see cref="RiskSignals"/>.</summary>
    public async Task<IReadOnlyList<RiskSignal>> RiskSignalsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        Domain.Project project = await dbContext.Projects.AsNoTracking().FirstAsync(row => row.Id == projectId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ProjectTask> open = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.CompletedDate == null && row.Type != TaskType.Epic)
            .ToListAsync(cancellationToken);
        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking().Where(row => row.ProjectId == projectId).ToListAsync(cancellationToken);
        WorkloadView workload = await WorkloadAsync(projectId, 2, cancellationToken);
        List<TaskDependency> links = await dbContext.TaskDependencies.AsNoTracking().Where(row => row.ProjectId == projectId).ToListAsync(cancellationToken);
        Dictionary<Guid, ProjectTask> byId = open.ToDictionary(row => row.Id);
        int broken = links.Count(link => byId.TryGetValue(link.PredecessorTaskId, out ProjectTask? before) && byId.TryGetValue(link.SuccessorTaskId, out ProjectTask? after)
            && link.Type == DependencyType.FinishToStart && after.StartDate < before.DueDate?.AddDays(link.LagDays));
        DateTime monthStart = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        int breaches = await dbContext.TicketSlas.AsNoTracking()
            .CountAsync(row => row.ProjectId == projectId && row.ExcludedReason == null && (row.ResponseBreachedAt >= monthStart || row.ResolutionBreachedAt >= monthStart), cancellationToken);

        return RiskSignals.From(new RiskInput(
            today,
            [.. open.Select(row => new RiskTask(row.Code, row.Title, row.AssigneeEmployeeId, row.StartDate, row.DueDate, row.EstimatedHours, row.MilestoneId))],
            [.. milestones.Select(row => new RiskMilestone(row.Id, row.Name, row.PlannedDate, row.ActualDate is not null))],
            [.. workload.People.Select(person => new RiskPerson(person.EmployeeName ?? "Someone", person.Weeks.Sum(week => week.PlannedHours), person.Weeks.Sum(week => week.CapacityHours)))],
            project.EndDate,
            project.BaselineEndDate,
            broken,
            breaches));
    }

    public async Task<ImportPreview> PreviewImportAsync(Guid projectId, ImportBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        IReadOnlyList<ImportRow> rows = await PlanAsync(projectId, body.Csv, cancellationToken);
        return new ImportPreview(
            [.. rows.Select(row => new ImportPreviewRow(row.Line, row.Key, row.Title, row.Type, row.Priority, row.AssigneeName, row.ColumnName, row.ParentKey, row.DueDate, row.AlreadyImported, row.Warnings, row.Errors))],
            rows.Count(WillCreate),
            rows.Count(row => row.AlreadyImported),
            rows.Count(row => row.Errors.Count > 0));
    }

    /// <summary>Creates every row that can be, parents first, all together. Rows with errors, and rows brought in before, are left out.</summary>
    public async Task<ImportResult> ImportAsync(Guid projectId, ImportBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        IReadOnlyList<ImportRow> rows = await PlanAsync(projectId, body.Csv, cancellationToken);
        List<ImportRow> creating = [.. rows.Where(WillCreate).OrderBy(row => row.Depth).ThenBy(row => row.Line)];

        Domain.Project project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);
        Dictionary<Guid, BoardColumn> columns = await dbContext.BoardColumns.Where(row => row.ProjectId == projectId).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, double> ranks = (await dbContext.ProjectTasks.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .GroupBy(row => row.ColumnId)
                .Select(group => new { group.Key, Rank = group.Max(row => row.Rank) })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Key, row => row.Rank);
        Dictionary<string, Guid> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (var known in await dbContext.ProjectTasks.AsNoTracking().Where(row => row.ProjectId == projectId).Select(row => new { row.Id, row.Code, row.ExternalKey }).ToListAsync(cancellationToken))
        {
            ids[known.Code] = known.Id;
            if (known.ExternalKey is { } external)
            {
                ids[external] = known.Id;
            }
        }

        foreach (ImportRow row in creating)
        {
            var details = new TaskDetails(
                row.Type, row.ParentKey is { } parent ? ids.GetValueOrDefault(parent) : null, null, null, null, row.Title, row.Description, row.AssigneeId, row.Priority,
                row.StartDate, row.DueDate, row.EstimatedHours, null, row.StoryPoints, 0, true);
            double rank = ranks.GetValueOrDefault(row.ColumnId) + 1000;
            ranks[row.ColumnId] = rank;

            var task = ProjectTask.Create(projectId, project.NextTaskNumber(), project.Code, details, columns[row.ColumnId], rank, access.Me, Now);
            task.MarkImported(row.Key);
            dbContext.ProjectTasks.Add(task);
            if (row.Key is { } key)
            {
                ids[key] = task.Id;
            }

            activity.Created(task, TaskFields.Imported);
            if (ServiceTickets.IsTicket(task.Type))
            {
                await serviceLevels.SyncAsync(task, cancellationToken);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new DomainRuleException("TASK_IMPORT_RACE", "Someone else has just imported tasks into this project. Check the list, then try again.");
        }

        return new ImportResult(creating.Count, rows.Count(row => row.AlreadyImported), rows.Count(row => row.Errors.Count > 0));
    }

    private static bool WillCreate(ImportRow row) => row.Errors.Count == 0 && !row.AlreadyImported;

    private async Task<IReadOnlyList<ImportRow>> PlanAsync(Guid projectId, string csv, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(csv))
        {
            throw new DomainRuleException("TASK_IMPORT_EMPTY", "That file is empty.");
        }

        List<Guid> teamIds = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.Status == TeamMemberStatus.Active)
            .Select(row => row.EmployeeId)
            .ToListAsync(cancellationToken);
        var managers = await dbContext.Projects.AsNoTracking()
            .Where(row => row.Id == projectId)
            .Select(row => new { row.ProjectManagerEmployeeId, row.DeliveryManagerEmployeeId, row.AccountManagerEmployeeId })
            .FirstAsync(cancellationToken);
        teamIds.AddRange(new[] { managers.ProjectManagerEmployeeId, managers.DeliveryManagerEmployeeId, managers.AccountManagerEmployeeId }.OfType<Guid>());
        IReadOnlyList<EmployeeSummary> team = await employees.GetManyAsync(teamIds.Distinct(), cancellationToken);

        List<ImportColumn> columns = await dbContext.BoardColumns.AsNoTracking()
            .Where(row => row.ProjectId == projectId)
            .OrderBy(row => row.SortOrder)
            .Select(row => new ImportColumn(row.Id, row.Name, row.Category))
            .ToListAsync(cancellationToken);
        List<ImportExisting> existing = await dbContext.ProjectTasks.AsNoTracking()
            .Where(row => row.ProjectId == projectId)
            .Select(row => new ImportExisting(row.Id, row.Code, row.ExternalKey, row.Type))
            .ToListAsync(cancellationToken);
        return TaskImport.Plan(csv, [.. team.Select(person => new ImportPerson(person.Id, person.DisplayName, person.OfficialEmail, person.EmployeeCode))], columns, existing);
    }
}
