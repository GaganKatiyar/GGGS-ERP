using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

/// <summary>
/// Portfolio analytics. Narrowed to the projects whose project, delivery or account manager is in
/// the caller's report reach, so a department head sees their department's projects.
/// </summary>
internal sealed class ProjectMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Projects];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IQueryable<ProjectEntity> query = dbContext.Projects.AsNoTracking();
        if (context.EmployeeScope is { } scope)
        {
            query = query.Where(row => (row.ProjectManagerEmployeeId != null && scope.Contains(row.ProjectManagerEmployeeId.Value))
                || (row.DeliveryManagerEmployeeId != null && scope.Contains(row.DeliveryManagerEmployeeId.Value))
                || (row.AccountManagerEmployeeId != null && scope.Contains(row.AccountManagerEmployeeId.Value)));
        }

        var projects = await query.Select(row => new { row.Id, row.Status, row.Stage, row.HealthStatus, row.EndDate, row.ActualEndDate }).ToListAsync(cancellationToken);
        List<Guid> ids = [.. projects.Select(row => row.Id)];
        var active = projects.Where(row => row.Status == ProjectStatus.Active).ToList();
        DateOnly today = context.Today;
        var milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && (row.Status == MilestoneStatus.Planned || row.Status == MilestoneStatus.InProgress))
            .Select(row => new { row.PlannedDate })
            .ToListAsync(cancellationToken);
        int risks = await dbContext.RaidItems.AsNoTracking().CountAsync(row => ids.Contains(row.ProjectId) && row.Kind == RaidKind.Risk && (row.Status == RaidStatus.Open || row.Status == RaidStatus.InProgress) && (row.Severity == Severity.High || row.Severity == Severity.Critical), cancellationToken);
        int changes = await dbContext.ChangeRequests.AsNoTracking().CountAsync(row => ids.Contains(row.ProjectId) && row.Status == ChangeRequestStatus.PendingApproval, cancellationToken);
        return
        [
            new Metric("projects.active", MetricAreas.Projects, "Active projects", active.Count, MetricUnit.Count, $"{projects.Count(row => row.Status == ProjectStatus.OnHold)} on hold",
                [.. active.GroupBy(row => row.Stage).OrderBy(group => group.Key).Select(group => new MetricPoint(group.Key.ToString(), group.Count()))], Executive: true),
            new Metric("projects.health", MetricAreas.Projects, "At risk (amber or red)", active.Count(row => row.HealthStatus is HealthRating.Amber or HealthRating.Red), MetricUnit.Count, null,
                [.. active.GroupBy(row => row.HealthStatus).OrderBy(group => group.Key).Select(group => new MetricPoint(group.Key == HealthRating.NotAssessed ? "Not assessed" : group.Key.ToString(), group.Count()))], Executive: true, HigherIsBetter: false),
            new Metric("projects.overdue", MetricAreas.Projects, "Running past their end date", active.Count(row => row.EndDate < today), MetricUnit.Count, null, HigherIsBetter: false),
            new Metric("projects.milestones", MetricAreas.Projects, "Milestones due (30 days)", milestones.Count(row => row.PlannedDate >= today && row.PlannedDate <= today.AddDays(30)), MetricUnit.Count,
                $"{milestones.Count(row => row.PlannedDate < today)} overdue"),
            new Metric("projects.risks", MetricAreas.Projects, "Open high risks", risks, MetricUnit.Count, $"{changes} change request(s) awaiting a decision", HigherIsBetter: false),
        ];
    }
}

/// <summary>Projects in global search, as far as the caller may see them.</summary>
internal sealed class ProjectSearch(AppDbContext dbContext, ProjectAccess access, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "project";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Project.View))
        {
            return [];
        }

        IQueryable<ProjectEntity> visible = await access.ApplyAsync(dbContext.Projects.AsNoTracking(), Permissions.Project.View, cancellationToken);
        var rows = await visible.Where(row => row.Name.Contains(term) || row.Code.Contains(term)).OrderBy(row => row.Name).Take(take)
            .Select(row => new { row.Id, row.Name, row.Code, row.Status, row.Stage }).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchHit(Kind, row.Id, row.Name, $"{row.Code} · {row.Stage} · {row.Status}", $"/projects/{row.Id}",
            row.Code.Equals(term, StringComparison.OrdinalIgnoreCase) || row.Name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1 : row.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.8 : 0.5))];
    }
}

/// <summary>Project milestones on the HR calendar: one's own on "mine", visible projects' otherwise.</summary>
internal sealed class ProjectCalendar(AppDbContext dbContext, ProjectAccess access, IPermissionProvider permissions, ICurrentUser currentUser) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IQueryable<ProjectMilestone> milestones = dbContext.ProjectMilestones.AsNoTracking()
            .Where(row => row.PlannedDate >= firstDay && row.PlannedDate <= lastDay && row.Status != MilestoneStatus.Cancelled);
        if (context.OnlyMine)
        {
            Guid? me = currentUser.EmployeeId;
            milestones = milestones.Where(row => row.OwnerEmployeeId == me);
        }
        else if ((await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Project.View))
        {
            List<Guid> visible = await (await access.ApplyAsync(dbContext.Projects.AsNoTracking(), Permissions.Project.View, cancellationToken)).Select(row => row.Id).ToListAsync(cancellationToken);
            milestones = milestones.Where(row => visible.Contains(row.ProjectId));
        }
        else
        {
            return [];
        }

        var rows = await (
                from milestone in milestones
                join project in dbContext.Projects.AsNoTracking() on milestone.ProjectId equals project.Id
                select new { milestone.Name, milestone.PlannedDate, milestone.Status, milestone.OwnerEmployeeId, project.Code, ProjectId = project.Id })
            .Take(500).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new CalendarEntry(CalendarKinds.Milestone, $"{row.Code}: {row.Name}", row.PlannedDate, row.PlannedDate, row.OwnerEmployeeId, $"/projects/{row.ProjectId}", row.Status.ToString()))];
    }
}
