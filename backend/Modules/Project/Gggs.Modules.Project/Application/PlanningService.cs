using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record MilestoneItem(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Name,
    string? Description,
    DateOnly PlannedDate,
    DateOnly? ActualDate,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    Guid? PhaseId,
    MilestoneStatus Status,
    int PercentComplete,
    decimal MeasuredPercent,
    int TaskCount,
    bool IsBillable,
    int DaysOverdue);

public sealed record MilestoneBody(
    string Name,
    string? Description,
    DateOnly PlannedDate,
    Guid? OwnerEmployeeId,
    Guid? PhaseId,
    MilestoneStatus? Status,
    int? PercentComplete,
    bool? IsBillable);

public sealed record AchieveBody(DateOnly? On);

public sealed record PortfolioMilestoneQuery
{
    public Guid? ProjectId { get; init; }

    /// <summary>Open milestones due within this many days (and overdue ones). Defaults to 30.</summary>
    public int? WithinDays { get; init; }

    public bool? OverdueOnly { get; init; }
}

public sealed record SprintItem(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Goal,
    int? CapacityPoints,
    SprintStatus Status,
    int? CommittedPoints,
    int? CompletedPoints,
    int PlannedPoints,
    int DonePoints,
    int TaskCount,
    int DoneTaskCount,
    string? ReviewNotes,
    string? RetrospectiveNotes);

public sealed record SprintBody(string Name, DateOnly StartDate, DateOnly EndDate, string? Goal, int? CapacityPoints, string? ReviewNotes, string? RetrospectiveNotes);

public sealed record CompleteSprintBody(Guid? MoveOpenTasksToSprintId);

public sealed record SprintTasksBody(IReadOnlyList<Guid> TaskIds);

public sealed record VelocityPoint(Guid SprintId, string Name, DateOnly EndDate, int Committed, int Completed);

public sealed record CalendarEvent(string Kind, Guid Id, string Title, DateOnly Start, DateOnly End, string? Status, string? Detail);

/// <summary>Milestones, sprints and the project calendar (spec §46, §48, §56).</summary>
internal sealed class PlanningService(
    AppDbContext dbContext,
    ProjectAccess access,
    ProjectService projects,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Milestones ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<MilestoneItem>> MilestonesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.MilestoneView, cancellationToken);
        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(milestone => milestone.ProjectId == projectId)
            .OrderBy(milestone => milestone.PlannedDate)
            .ToListAsync(cancellationToken);
        return await ToMilestoneItemsAsync(milestones, withProject: false, cancellationToken);
    }

    public async Task<IReadOnlyList<MilestoneItem>> PortfolioMilestonesAsync(PortfolioMilestoneQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.MilestoneView, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly horizon = today.AddDays(Math.Clamp(query.WithinDays ?? 30, 0, 366));

        IQueryable<ProjectMilestone> rows = dbContext.ProjectMilestones.AsNoTracking()
            .Where(milestone => milestone.Status == MilestoneStatus.Planned || milestone.Status == MilestoneStatus.InProgress);
        if (visible is not null)
        {
            rows = rows.Where(milestone => visible.Contains(milestone.ProjectId));
        }

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(milestone => milestone.ProjectId == projectId);
        }

        rows = query.OverdueOnly == true ? rows.Where(milestone => milestone.PlannedDate < today) : rows.Where(milestone => milestone.PlannedDate <= horizon);
        List<ProjectMilestone> milestones = await rows.OrderBy(milestone => milestone.PlannedDate).Take(500).ToListAsync(cancellationToken);
        return await ToMilestoneItemsAsync(milestones, withProject: true, cancellationToken);
    }

    public async Task<MilestoneItem> SaveMilestoneAsync(Guid projectId, Guid? milestoneId, MilestoneBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.MilestoneManage, cancellationToken);
        await projects.EnsureEmployeesAsync([body.OwnerEmployeeId], cancellationToken);

        if (body.PhaseId is { } phaseId && !await dbContext.ProjectPhases.AnyAsync(phase => phase.Id == phaseId && phase.ProjectId == projectId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_MILESTONE_PHASE", "The phase is not in this project.");
        }

        ProjectMilestone milestone;
        if (milestoneId is { } id)
        {
            milestone = await FindMilestoneAsync(projectId, id, cancellationToken);
            milestone.Update(new MilestonePlan(
                body.Name, body.Description, body.PlannedDate, body.OwnerEmployeeId, body.PhaseId, body.Status ?? milestone.Status,
                body.PercentComplete ?? milestone.PercentComplete, body.IsBillable ?? milestone.IsBillable));
        }
        else
        {
            milestone = ProjectMilestone.Create(projectId, new MilestonePlan(
                body.Name, body.Description, body.PlannedDate, body.OwnerEmployeeId, body.PhaseId, body.Status ?? MilestoneStatus.Planned,
                body.PercentComplete ?? 0, body.IsBillable ?? false));
            dbContext.ProjectMilestones.Add(milestone);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToMilestoneItemsAsync([milestone], withProject: false, cancellationToken))[0];
    }

    public async Task<MilestoneItem> AchieveMilestoneAsync(Guid projectId, Guid milestoneId, DateOnly? on, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.MilestoneManage, cancellationToken);
        ProjectMilestone milestone = await FindMilestoneAsync(projectId, milestoneId, cancellationToken);
        milestone.Achieve(on ?? await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToMilestoneItemsAsync([milestone], withProject: false, cancellationToken))[0];
    }

    public async Task DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.MilestoneManage, cancellationToken);
        ProjectMilestone milestone = await FindMilestoneAsync(projectId, milestoneId, cancellationToken);

        // Tasks aimed at the milestone simply lose the link; the work itself stands.
        List<ProjectTask> linked = await dbContext.ProjectTasks.Where(task => task.MilestoneId == milestoneId).ToListAsync(cancellationToken);
        foreach (ProjectTask task in linked)
        {
            task.Update(Detach(task, milestone: true));
        }

        dbContext.ProjectMilestones.Remove(milestone);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Sprints ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SprintItem>> SprintsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintView, cancellationToken);
        List<Sprint> sprints = await dbContext.Sprints.AsNoTracking()
            .Where(sprint => sprint.ProjectId == projectId)
            .OrderByDescending(sprint => sprint.StartDate)
            .ToListAsync(cancellationToken);
        return await ToSprintItemsAsync(sprints, withProject: false, cancellationToken);
    }

    public async Task<IReadOnlyList<SprintItem>> PortfolioSprintsAsync(SprintStatus? status, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.SprintView, cancellationToken);
        SprintStatus wanted = status ?? SprintStatus.Active;
        IQueryable<Sprint> rows = dbContext.Sprints.AsNoTracking().Where(sprint => sprint.Status == wanted);
        if (visible is not null)
        {
            rows = rows.Where(sprint => visible.Contains(sprint.ProjectId));
        }

        List<Sprint> sprints = await rows.OrderBy(sprint => sprint.EndDate).Take(300).ToListAsync(cancellationToken);
        return await ToSprintItemsAsync(sprints, withProject: true, cancellationToken);
    }

    public async Task<SprintItem> SaveSprintAsync(Guid projectId, Guid? sprintId, SprintBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);

        Sprint sprint;
        if (sprintId is { } id)
        {
            sprint = await FindSprintAsync(projectId, id, cancellationToken);
            sprint.Update(body.Name, body.StartDate, body.EndDate, body.Goal, body.CapacityPoints, body.ReviewNotes, body.RetrospectiveNotes);
        }
        else
        {
            sprint = Sprint.Plan(projectId, body.Name, body.StartDate, body.EndDate, body.Goal, body.CapacityPoints);
            dbContext.Sprints.Add(sprint);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSprintItemsAsync([sprint], withProject: false, cancellationToken))[0];
    }

    /// <summary>Puts backlog tasks into a sprint (sprint planning).</summary>
    public async Task<SprintItem> PlanTasksAsync(Guid projectId, Guid sprintId, IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);
        Sprint sprint = await FindSprintAsync(projectId, sprintId, cancellationToken);
        if (!sprint.IsOpen)
        {
            throw new DomainRuleException("PROJECT_SPRINT_STATE", "Only an open sprint can take more work.");
        }

        List<ProjectTask> tasks = await dbContext.ProjectTasks.Where(task => task.ProjectId == projectId && taskIds.Contains(task.Id)).ToListAsync(cancellationToken);
        if (tasks.Count != taskIds.Distinct().Count())
        {
            throw new DomainRuleException("PROJECT_SPRINT_TASKS", "Some of those tasks are not in this project.");
        }

        if (tasks.Any(task => task.Type == TaskType.Epic))
        {
            throw new DomainRuleException("PROJECT_SPRINT_EPIC", "Epics are too big for a sprint; plan their stories instead.");
        }

        BoardColumn? todo = await dbContext.BoardColumns.Where(column => column.ProjectId == projectId && column.Category == ColumnCategory.Todo).OrderBy(column => column.SortOrder).FirstOrDefaultAsync(cancellationToken);
        Dictionary<Guid, ColumnCategory> categories = await dbContext.BoardColumns.AsNoTracking().Where(column => column.ProjectId == projectId).ToDictionaryAsync(column => column.Id, column => column.Category, cancellationToken);

        foreach (ProjectTask task in tasks)
        {
            task.PlanInto(sprint.Id);

            // Pulled from the backlog into a sprint, a task is ready to start.
            if (todo is not null && categories.GetValueOrDefault(task.ColumnId) == ColumnCategory.Backlog)
            {
                task.MoveTo(todo, task.Rank, Now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSprintItemsAsync([sprint], withProject: false, cancellationToken))[0];
    }

    public async Task<SprintItem> UnplanTaskAsync(Guid projectId, Guid sprintId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);
        Sprint sprint = await FindSprintAsync(projectId, sprintId, cancellationToken);
        ProjectTask task = await dbContext.ProjectTasks.FirstOrDefaultAsync(row => row.Id == taskId && row.SprintId == sprintId, cancellationToken)
            ?? throw new NotFoundException("Task", taskId);
        task.PlanInto(null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSprintItemsAsync([sprint], withProject: false, cancellationToken))[0];
    }

    public async Task<SprintItem> StartSprintAsync(Guid projectId, Guid sprintId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);
        Sprint sprint = await FindSprintAsync(projectId, sprintId, cancellationToken);

        if (await dbContext.Sprints.AnyAsync(row => row.ProjectId == projectId && row.Status == SprintStatus.Active && row.Id != sprintId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_SPRINT_RUNNING", "Another sprint is already running; complete it first.");
        }

        int committed = await dbContext.ProjectTasks.Where(task => task.SprintId == sprintId).SumAsync(task => task.StoryPoints ?? 0, cancellationToken);
        sprint.Start(committed, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSprintItemsAsync([sprint], withProject: false, cancellationToken))[0];
    }

    /// <summary>
    /// Ends the active sprint: records its velocity and burndown, and sends unfinished work to the next
    /// sprint named, or back to the backlog.
    /// </summary>
    public async Task<SprintItem> CompleteSprintAsync(Guid projectId, Guid sprintId, Guid? moveTo, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);
        Sprint sprint = await FindSprintAsync(projectId, sprintId, cancellationToken);

        if (moveTo is { } next && !await dbContext.Sprints.AnyAsync(row => row.Id == next && row.ProjectId == projectId && row.Status == SprintStatus.Planned, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_SPRINT_NEXT", "Unfinished work can only move to a planned sprint of this project.");
        }

        List<ProjectTask> tasks = await dbContext.ProjectTasks.Where(task => task.SprintId == sprintId).ToListAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyList<BurndownPoint> burndown = await BurndownForAsync(sprint, tasks, today, cancellationToken);
        int completed = tasks.Where(task => task.IsDone).Sum(task => task.StoryPoints ?? 0);

        sprint.Complete(completed, JsonSerializer.Serialize(burndown), Now);
        foreach (ProjectTask open in tasks.Where(task => !task.IsDone))
        {
            open.PlanInto(moveTo);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSprintItemsAsync([sprint], withProject: false, cancellationToken))[0];
    }

    public async Task CancelSprintAsync(Guid projectId, Guid sprintId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintManage, cancellationToken);
        Sprint sprint = await FindSprintAsync(projectId, sprintId, cancellationToken);
        sprint.Cancel();

        List<ProjectTask> tasks = await dbContext.ProjectTasks.Where(task => task.SprintId == sprintId && task.CompletedDate == null).ToListAsync(cancellationToken);
        foreach (ProjectTask task in tasks)
        {
            task.PlanInto(null);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BurndownPoint>> BurndownAsync(Guid projectId, Guid sprintId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintView, cancellationToken);
        Sprint sprint = await dbContext.Sprints.AsNoTracking().FirstOrDefaultAsync(row => row.Id == sprintId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), sprintId);

        if (sprint.BurndownJson is { } stored)
        {
            return JsonSerializer.Deserialize<List<BurndownPoint>>(stored) ?? [];
        }

        List<ProjectTask> tasks = await dbContext.ProjectTasks.AsNoTracking().Where(task => task.SprintId == sprintId).ToListAsync(cancellationToken);
        return await BurndownForAsync(sprint, tasks, await clock.TodayAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<VelocityPoint>> VelocityAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.SprintView, cancellationToken);
        return await dbContext.Sprints.AsNoTracking()
            .Where(sprint => sprint.ProjectId == projectId && sprint.Status == SprintStatus.Completed)
            .OrderByDescending(sprint => sprint.EndDate)
            .Take(12)
            .OrderBy(sprint => sprint.EndDate)
            .Select(sprint => new VelocityPoint(sprint.Id, sprint.Name, sprint.EndDate, sprint.CommittedPoints ?? 0, sprint.CompletedPoints ?? 0))
            .ToListAsync(cancellationToken);
    }

    // ----- Calendar -----------------------------------------------------------------------------

    /// <summary>Everything dated in a window (spec §56): phases, milestones, sprints, meetings and task deadlines.</summary>
    public async Task<IReadOnlyList<CalendarEvent>> CalendarAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.View, cancellationToken);
        if (to < from || to.DayNumber - from.DayNumber > 120)
        {
            throw new DomainRuleException("PROJECT_CALENDAR_RANGE", "Ask for up to 120 days at a time.");
        }

        List<CalendarEvent> events = [];

        events.AddRange((await dbContext.ProjectPhases.AsNoTracking()
                .Where(phase => phase.ProjectId == projectId && phase.StartDate != null && phase.EndDate != null && phase.StartDate <= to && phase.EndDate >= from)
                .ToListAsync(cancellationToken))
            .Select(phase => new CalendarEvent("Phase", phase.Id, phase.Name, phase.StartDate!.Value, phase.EndDate!.Value, phase.Status.ToString(), null)));

        events.AddRange((await dbContext.ProjectMilestones.AsNoTracking()
                .Where(milestone => milestone.ProjectId == projectId && milestone.PlannedDate >= from && milestone.PlannedDate <= to)
                .ToListAsync(cancellationToken))
            .Select(milestone => new CalendarEvent("Milestone", milestone.Id, milestone.Name, milestone.PlannedDate, milestone.PlannedDate, milestone.Status.ToString(), null)));

        events.AddRange((await dbContext.Sprints.AsNoTracking()
                .Where(sprint => sprint.ProjectId == projectId && sprint.Status != SprintStatus.Cancelled && sprint.StartDate <= to && sprint.EndDate >= from)
                .ToListAsync(cancellationToken))
            .Select(sprint => new CalendarEvent("Sprint", sprint.Id, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status.ToString(), sprint.Goal)));

        events.AddRange((await dbContext.ProjectMeetings.AsNoTracking()
                .Where(meeting => meeting.ProjectId == projectId && meeting.Status != MeetingStatus.Cancelled && meeting.Date >= from && meeting.Date <= to)
                .ToListAsync(cancellationToken))
            .Select(meeting => new CalendarEvent("Meeting", meeting.Id, meeting.Title, meeting.Date, meeting.Date, meeting.Status.ToString(), meeting.StartTime?.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture))));

        events.AddRange((await dbContext.ProjectTasks.AsNoTracking()
                .Where(task => task.ProjectId == projectId && task.DueDate != null && task.DueDate >= from && task.DueDate <= to && task.Type != TaskType.Epic)
                .OrderBy(task => task.DueDate)
                .Take(300)
                .ToListAsync(cancellationToken))
            .Select(task => new CalendarEvent("Task", task.Id, $"{task.Code} {task.Title}", task.DueDate!.Value, task.DueDate!.Value, task.IsDone ? "Done" : "Open", null)));

        return [.. events.OrderBy(item => item.Start).ThenBy(item => item.Kind, StringComparer.Ordinal)];
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<IReadOnlyList<BurndownPoint>> BurndownForAsync(Sprint sprint, IReadOnlyList<ProjectTask> tasks, DateOnly today, CancellationToken cancellationToken)
    {
        List<(int, DateOnly?)> work = [];
        foreach (ProjectTask task in tasks)
        {
            DateOnly? completedOn = task.CompletedDate is { } at ? DateOnly.FromDateTime(await ToLocalAsync(at, cancellationToken)) : null;
            work.Add((task.StoryPoints ?? 0, completedOn));
        }

        return Burndown.Compute(sprint.StartDate, sprint.EndDate, today, work);
    }

    private async Task<DateTime> ToLocalAsync(DateTime utc, CancellationToken cancellationToken)
        => await clock.ToLocalAsync(utc, cancellationToken);

    private static TaskDetails Detach(ProjectTask task, bool milestone)
        => new(task.Type, task.ParentTaskId, task.PhaseId, milestone ? null : task.MilestoneId, task.RequirementId, task.Title, task.Description, task.AssigneeEmployeeId,
            task.Priority, task.StartDate, task.DueDate, task.EstimatedHours, task.ActualHours, task.StoryPoints, task.Progress, task.Billable);

    private async Task<ProjectMilestone> FindMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken)
        => await dbContext.ProjectMilestones.FirstOrDefaultAsync(row => row.Id == milestoneId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectMilestone), milestoneId);

    private async Task<Sprint> FindSprintAsync(Guid projectId, Guid sprintId, CancellationToken cancellationToken)
        => await dbContext.Sprints.FirstOrDefaultAsync(row => row.Id == sprintId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), sprintId);

    private async Task<Dictionary<Guid, (string Code, string Name)>> ProjectNamesAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. projectIds.Distinct()];
        return await dbContext.Projects.AsNoTracking().Where(project => ids.Contains(project.Id)).ToDictionaryAsync(project => project.Id, project => (project.Code, project.Name), cancellationToken);
    }

    private async Task<List<MilestoneItem>> ToMilestoneItemsAsync(IReadOnlyList<ProjectMilestone> milestones, bool withProject, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> ids = [.. milestones.Select(milestone => milestone.Id)];
        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.MilestoneId != null && ids.Contains(task.MilestoneId.Value))
            .Select(task => new { task.MilestoneId, task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> owners = await projects.NamesAsync(milestones.Select(milestone => milestone.OwnerEmployeeId), cancellationToken);
        Dictionary<Guid, (string Code, string Name)> names = withProject ? await ProjectNamesAsync(milestones.Select(milestone => milestone.ProjectId), cancellationToken) : [];

        return [.. milestones.Select(milestone =>
        {
            var mine = tasks.Where(task => task.MilestoneId == milestone.Id).ToList();
            (decimal measured, _) = ProgressMeter.Of(mine.Select(task => (task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Done)));
            (string Code, string Name)? project = names.TryGetValue(milestone.ProjectId, out (string Code, string Name) found) ? found : null;
            return new MilestoneItem(
                milestone.Id, milestone.ProjectId, project?.Code, project?.Name, milestone.Name, milestone.Description, milestone.PlannedDate,
                milestone.ActualDate, milestone.OwnerEmployeeId, milestone.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null,
                milestone.PhaseId, milestone.Status, milestone.PercentComplete,
                milestone.Status == MilestoneStatus.Achieved ? 100 : mine.Count == 0 ? milestone.PercentComplete : measured,
                mine.Count, milestone.IsBillable, milestone.DaysOverdue(today));
        })];
    }

    private async Task<List<SprintItem>> ToSprintItemsAsync(IReadOnlyList<Sprint> sprints, bool withProject, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. sprints.Select(sprint => sprint.Id)];
        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.SprintId != null && ids.Contains(task.SprintId.Value))
            .Select(task => new { task.SprintId, Points = task.StoryPoints ?? 0, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, (string Code, string Name)> names = withProject ? await ProjectNamesAsync(sprints.Select(sprint => sprint.ProjectId), cancellationToken) : [];

        return [.. sprints.Select(sprint =>
        {
            var mine = tasks.Where(task => task.SprintId == sprint.Id).ToList();
            (string Code, string Name)? project = names.TryGetValue(sprint.ProjectId, out (string Code, string Name) found) ? found : null;
            return new SprintItem(
                sprint.Id, sprint.ProjectId, project?.Code, project?.Name, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Goal, sprint.CapacityPoints,
                sprint.Status, sprint.CommittedPoints, sprint.CompletedPoints, mine.Sum(task => task.Points), mine.Where(task => task.Done).Sum(task => task.Points),
                mine.Count, mine.Count(task => task.Done), sprint.ReviewNotes, sprint.RetrospectiveNotes);
        })];
    }
}
