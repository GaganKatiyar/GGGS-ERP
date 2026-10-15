using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

public sealed record HealthTrendPoint(DateOnly AssessedOn, HealthRating Overall, decimal Score, decimal ProgressPercent);

public sealed record HealthView(HealthResult Current, DateOnly AssessedOn, IReadOnlyList<HealthTrendPoint> Trend);

public sealed record TaskCounts(int Total, int Open, int Done, int Overdue, IReadOnlyDictionary<string, int> ByCategory, IReadOnlyDictionary<string, int> ByType);

public sealed record RaidCounts(int OpenRisks, int HighRisks, int OpenIssues, int CriticalIssues, int OpenAssumptions, int OpenDependencies);

public sealed record ProjectDashboard(
    ProjectItem Project,
    HealthView Health,
    decimal ElapsedPercent,
    TaskCounts Tasks,
    IReadOnlyList<MilestoneItem> Milestones,
    int MilestonesAchieved,
    int MilestonesTotal,
    RaidCounts Raid,
    int PendingChangeRequests,
    int OpenActions,
    SprintItem? ActiveSprint,
    IReadOnlyList<BurndownPoint> Burndown,
    IReadOnlyList<VelocityPoint> Velocity,
    IReadOnlyList<PhaseItem> Phases);

/// <summary>Project health (spec §78) and the project dashboard (spec §79, §120).</summary>
internal sealed class InsightService(
    AppDbContext dbContext,
    ProjectAccess access,
    ProjectService projects,
    PlanningService planning,
    ITenantClock clock)
{
    public async Task<HealthView> HealthAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ProjectEntity project = await access.LoadAsync(projectId, Permissions.Project.View, track: false, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        HealthResult current = HealthCalculator.Assess(await GatherAsync(project, today, cancellationToken), await projects.ThresholdsAsync(cancellationToken));
        return new HealthView(current, today, await TrendAsync(projectId, cancellationToken));
    }

    /// <summary>Assesses one project now and keeps today's verdict (replacing an earlier one from today).</summary>
    public async Task<HealthView> AssessAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        ProjectEntity project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        HealthResult result = await RecordAsync(project, today, await projects.ThresholdsAsync(cancellationToken), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new HealthView(result, today, await TrendAsync(projectId, cancellationToken));
    }

    /// <summary>The nightly sweep for the current tenant: every open project's health, recorded.</summary>
    public async Task<int> AssessAllAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        ProjectHealthThresholds thresholds = await projects.ThresholdsAsync(cancellationToken);
        List<ProjectEntity> open = await dbContext.Projects
            .Where(project => project.Status == ProjectStatus.Active || project.Status == ProjectStatus.OnHold)
            .ToListAsync(cancellationToken);

        foreach (ProjectEntity project in open)
        {
            await RecordAsync(project, today, thresholds, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return open.Count;
    }

    public async Task<ProjectDashboard> DashboardAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ProjectDetail detail = await projects.GetAsync(projectId, cancellationToken);
        ProjectEntity project = await dbContext.Projects.AsNoTracking().FirstAsync(row => row.Id == projectId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);

        HealthResult health = HealthCalculator.Assess(await GatherAsync(project, today, cancellationToken), await projects.ThresholdsAsync(cancellationToken));

        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId && task.Type != TaskType.Epic)
            .Join(dbContext.BoardColumns.AsNoTracking(), task => task.ColumnId, column => column.Id, (task, column) => new { task.Type, column.Category, task.DueDate, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        var counts = new TaskCounts(
            tasks.Count,
            tasks.Count(task => !task.Done),
            tasks.Count(task => task.Done),
            tasks.Count(task => !task.Done && task.DueDate is { } due && due < today),
            tasks.GroupBy(task => task.Category.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            tasks.GroupBy(task => task.Type.ToString()).ToDictionary(group => group.Key, group => group.Count()));

        IReadOnlyList<MilestoneItem> milestones = await planning.MilestonesAsync(projectId, cancellationToken);
        var raid = await dbContext.RaidItems.AsNoTracking()
            .Where(item => item.ProjectId == projectId && (item.Status == RaidStatus.Open || item.Status == RaidStatus.InProgress))
            .Select(item => new { item.Kind, item.Score, item.Severity })
            .ToListAsync(cancellationToken);

        SprintItem? active = (await planning.SprintsAsync(projectId, cancellationToken)).FirstOrDefault(sprint => sprint.Status == SprintStatus.Active);

        return new ProjectDashboard(
            detail.Project,
            new HealthView(health, today, await TrendAsync(projectId, cancellationToken)),
            HealthCalculator.ElapsedPercent(today, project.StartDate, project.TargetEndDate),
            counts,
            [.. milestones.Where(milestone => milestone.Status is MilestoneStatus.Planned or MilestoneStatus.InProgress).Take(6)],
            milestones.Count(milestone => milestone.Status == MilestoneStatus.Achieved),
            milestones.Count(milestone => milestone.Status != MilestoneStatus.Cancelled),
            new RaidCounts(
                raid.Count(item => item.Kind == RaidKind.Risk),
                raid.Count(item => item.Kind == RaidKind.Risk && item.Score >= 15),
                raid.Count(item => item.Kind == RaidKind.Issue),
                raid.Count(item => item.Kind == RaidKind.Issue && item.Severity == Severity.Critical),
                raid.Count(item => item.Kind == RaidKind.Assumption),
                raid.Count(item => item.Kind == RaidKind.Dependency)),
            await dbContext.ChangeRequests.CountAsync(request => request.ProjectId == projectId && request.Status == ChangeRequestStatus.PendingApproval, cancellationToken),
            await dbContext.MeetingActions.CountAsync(action => action.ProjectId == projectId && action.Status == ActionStatus.Open, cancellationToken),
            active,
            active is null ? [] : await planning.BurndownAsync(projectId, active.Id, cancellationToken),
            await planning.VelocityAsync(projectId, cancellationToken),
            await projects.PhasesAsync(projectId, cancellationToken));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<HealthResult> RecordAsync(ProjectEntity project, DateOnly today, ProjectHealthThresholds thresholds, CancellationToken cancellationToken)
    {
        HealthResult result = HealthCalculator.Assess(await GatherAsync(project, today, cancellationToken), thresholds);
        ProjectHealthSnapshot? existing = await dbContext.ProjectHealthSnapshots.FirstOrDefaultAsync(row => row.ProjectId == project.Id && row.AssessedOn == today, cancellationToken);
        if (existing is null)
        {
            dbContext.ProjectHealthSnapshots.Add(ProjectHealthSnapshot.Record(project.Id, today, result));
        }
        else
        {
            existing.Refresh(result);
        }

        project.RecordHealth(result.Overall, today);
        return result;
    }

    private async Task<IReadOnlyList<HealthTrendPoint>> TrendAsync(Guid projectId, CancellationToken cancellationToken)
        => await dbContext.ProjectHealthSnapshots.AsNoTracking()
            .Where(row => row.ProjectId == projectId)
            .OrderByDescending(row => row.AssessedOn)
            .Take(30)
            .OrderBy(row => row.AssessedOn)
            .Select(row => new HealthTrendPoint(row.AssessedOn, row.Overall, row.Score, row.ProgressPercent))
            .ToListAsync(cancellationToken);

    /// <summary>Collects what the health rules look at for one project.</summary>
    internal async Task<HealthInputs> GatherAsync(ProjectEntity project, DateOnly today, CancellationToken cancellationToken)
    {
        Guid id = project.Id;
        ProjectHealthThresholds thresholds = await projects.ThresholdsAsync(cancellationToken);

        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == id)
            .Select(task => new { task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Priority, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        (decimal progress, int measured) = ProgressMeter.Of(tasks.Select(task => (task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Done)));

        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(milestone => milestone.ProjectId == id && milestone.Status != MilestoneStatus.Cancelled)
            .ToListAsync(cancellationToken);

        List<ProjectTeamMember> team = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.ProjectId == id && member.Status == TeamMemberStatus.Active)
            .ToListAsync(cancellationToken);
        List<ProjectTeamMember> onNow = [.. team.Where(member => member.IsOnProject(today))];
        DateOnly soon = today.AddDays(thresholds.ResourceEndingSoonDays);
        int endingSoon = onNow.Count(member => member.EndDate is { } end && end <= soon && end < project.TargetEndDate);

        var changes = await dbContext.ChangeRequests.AsNoTracking()
            .Where(request => request.ProjectId == id)
            .Select(request => new { request.Status, request.ScheduleImpactDays })
            .ToListAsync(cancellationToken);
        var raid = await dbContext.RaidItems.AsNoTracking()
            .Where(item => item.ProjectId == id && (item.Status == RaidStatus.Open || item.Status == RaidStatus.InProgress))
            .Select(item => new { item.Kind, item.Score, item.Severity, item.DueDate })
            .ToListAsync(cancellationToken);

        var openBugs = tasks.Where(task => task.Type == TaskType.Bug && !task.Done).ToList();
        var issues = raid.Where(item => item.Kind == RaidKind.Issue).ToList();
        var risks = raid.Where(item => item.Kind == RaidKind.Risk).ToList();

        return new HealthInputs(
            today,
            project.StartDate,
            project.TargetEndDate,
            progress,
            measured,
            [.. milestones.Select(milestone => milestone.DaysOverdue(today)).Where(days => days > 0)],
            milestones.Count,
            onNow.Select(member => member.EmployeeId).Distinct().Count(),
            project.ProjectManagerEmployeeId is not null,
            endingSoon,
            changes.Count(request => request.Status == ChangeRequestStatus.PendingApproval),
            changes.Where(request => request.Status is ChangeRequestStatus.Approved or ChangeRequestStatus.Implementing or ChangeRequestStatus.Testing or ChangeRequestStatus.Closed)
                .Sum(request => Math.Max(0, request.ScheduleImpactDays ?? 0)),
            openBugs.Count,
            openBugs.Count(task => task.Priority == WorkPriority.High),
            openBugs.Count(task => task.Priority == WorkPriority.Critical),
            risks.Count == 0 ? 0 : risks.Max(item => item.Score ?? 0),
            risks.Count,
            issues.Count(item => item.Severity == Severity.Critical),
            issues.Count(item => item.Severity == Severity.High),
            issues.Count(item => item.DueDate is { } due && due < today),
            project.ClientSatisfaction);
    }
}
