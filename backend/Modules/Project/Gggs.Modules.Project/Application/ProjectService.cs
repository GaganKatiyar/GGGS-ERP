using System.Globalization;
using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Contract.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

public sealed record ProjectListQuery
{
    public string? Search { get; init; }

    public ProjectStatus? Status { get; init; }

    public ProjectStage? Stage { get; init; }

    public HealthRating? Health { get; init; }

    public ProjectType? Type { get; init; }

    public Guid? ClientId { get; init; }

    public Guid? ManagerEmployeeId { get; init; }

    /// <summary>Only projects the caller manages or is on, even when they can see more.</summary>
    public bool? Mine { get; init; }

    /// <summary>Completed and cancelled projects are left out unless asked for (or filtered by status).</summary>
    public bool? IncludeClosed { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Sort = Sort, Search = Search };
}

public sealed record ProjectItem(
    Guid Id,
    string Code,
    string Name,
    ProjectType Type,
    string? Category,
    Guid? ClientId,
    string? ClientName,
    Guid? ContractId,
    string? ContractNumber,
    Guid? BusinessUnitId,
    string? BusinessUnitName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? ProjectManagerEmployeeId,
    string? ProjectManagerName,
    Guid? DeliveryManagerEmployeeId,
    string? DeliveryManagerName,
    Guid? AccountManagerEmployeeId,
    string? AccountManagerName,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? PlannedEndDate,
    DateOnly? ActualEndDate,
    WorkPriority Priority,
    ProjectStatus Status,
    ProjectStage Stage,
    HealthRating HealthStatus,
    DateOnly? HealthAssessedOn,
    ProjectBillingType BillingType,
    string Currency,
    decimal? Budget,
    decimal? RevenueTarget,
    decimal? CostBudget,
    string? Description,
    Guid? LocationId,
    string? LocationName,
    string? WorkModeCode,
    Confidentiality Confidentiality,
    DeliveryMethodology Methodology,
    byte? ClientSatisfaction,
    decimal ProgressPercent,
    int TeamSize,
    int OpenTasks);

/// <summary>What the caller may do on this project, so the UI shows only the buttons that work.</summary>
public sealed record ProjectAbilities(
    bool Manage,
    bool ManageTasks,
    bool ManageMilestones,
    bool ManageSprints,
    bool ManageChangeRequests,
    bool ManageRaid,
    bool SeeRates);

public sealed record ProjectDetail(ProjectItem Project, ProjectAbilities Can);

public sealed record ProjectRequest(
    string Code,
    string Name,
    ProjectType Type,
    string? Category,
    Guid? ClientId,
    Guid? ContractId,
    Guid? BusinessUnitId,
    Guid? DepartmentId,
    Guid? ProjectManagerEmployeeId,
    Guid? DeliveryManagerEmployeeId,
    Guid? AccountManagerEmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? PlannedEndDate,
    WorkPriority Priority,
    ProjectBillingType BillingType,
    string Currency,
    decimal? Budget,
    decimal? RevenueTarget,
    decimal? CostBudget,
    string? Description,
    Guid? LocationId,
    string? WorkModeCode,
    Confidentiality Confidentiality,
    DeliveryMethodology Methodology,
    ProjectStage? Stage);

public sealed record StageBody(ProjectStage Stage, string? Comment);

public sealed record StatusBody(ProjectStatus Status, string? Comment);

public sealed record SatisfactionBody(byte Score, string? Comment);

public sealed record HistoryItem(Guid Id, string Kind, string FromValue, string ToValue, string? Comment, Guid? ChangedByEmployeeId, string? ChangedByName, DateTime ChangedDate);

public sealed record PortfolioSummary(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> ByStage,
    IReadOnlyDictionary<string, int> ByHealth,
    int OverdueMilestones,
    int OpenHighRisks,
    int PendingChangeRequests);

public sealed record TeamMemberItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string? Designation,
    Guid ProjectRoleId,
    string? RoleName,
    DateOnly StartDate,
    DateOnly? EndDate,
    int AllocationPercent,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    TeamMemberStatus Status,
    string? Notes,
    int TotalAllocationPercent,
    bool OverAllocated);

public sealed record TeamMemberBody(
    Guid EmployeeId,
    Guid ProjectRoleId,
    DateOnly StartDate,
    DateOnly? EndDate,
    int AllocationPercent,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    string? Notes);

public sealed record ReleaseBody(DateOnly On);

public sealed record PhaseItem(
    Guid Id,
    string Name,
    int SortOrder,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    decimal? Budget,
    PhaseStatus Status,
    int Progress,
    decimal MeasuredProgress,
    int TaskCount);

public sealed record PhaseBody(string Name, int? SortOrder, DateOnly? StartDate, DateOnly? EndDate, Guid? OwnerEmployeeId, decimal? Budget, PhaseStatus Status, int Progress);

public sealed record ColumnItem(Guid Id, string Name, int SortOrder, ColumnCategory Category, int? WipLimit, int TaskCount);

public sealed record ColumnBody(Guid? Id, string Name, ColumnCategory Category, int? WipLimit);

public sealed record RoleItem(Guid Id, string Code, string Name, string? Description, IReadOnlyList<string> Skills, bool IsSystem, bool IsActive, int SortOrder);

public sealed record RoleBody(string Code, string Name, string? Description, IReadOnlyList<string>? Skills, int? SortOrder);

/// <summary>
/// Projects (spec §39-43, §45): the master record and its lifecycle, the team, phases, the board's
/// columns, and the tenant-wide project roles and health thresholds.
/// </summary>
internal sealed class ProjectService(
    AppDbContext dbContext,
    ProjectAccess access,
    IClientDirectory clients,
    IContractDirectory contracts,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    IMasterDataLookup masterData,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private static readonly Dictionary<string, Expression<Func<ProjectEntity, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = project => project.Name,
        ["code"] = project => project.Code,
        ["startDate"] = project => project.StartDate,
        ["endDate"] = project => project.EndDate,
        ["status"] = project => project.Status,
        ["stage"] = project => project.Stage,
        ["health"] = project => project.HealthStatus,
        ["priority"] = project => project.Priority,
    };

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Projects -----------------------------------------------------------------------------

    public async Task<PagedResult<ProjectItem>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IQueryable<ProjectEntity> rows = await access.ApplyAsync(dbContext.Projects.AsNoTracking(), Permissions.Project.View, cancellationToken);

        if (query.Mine == true)
        {
            Guid me = access.RequireEmployee();
            List<Guid> memberOf = await dbContext.ProjectTeamMembers.AsNoTracking().Where(row => row.EmployeeId == me).Select(row => row.ProjectId).ToListAsync(cancellationToken);
            rows = rows.Where(project => project.ProjectManagerEmployeeId == me || project.DeliveryManagerEmployeeId == me
                || project.AccountManagerEmployeeId == me || memberOf.Contains(project.Id));
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(project => project.Name.Contains(term) || project.Code.Contains(term));
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(project => project.Status == status);
        }
        else if (query.IncludeClosed != true)
        {
            rows = rows.Where(project => project.Status == ProjectStatus.Active || project.Status == ProjectStatus.OnHold);
        }

        if (query.Stage is { } stage)
        {
            rows = rows.Where(project => project.Stage == stage);
        }

        if (query.Health is { } health)
        {
            rows = rows.Where(project => project.HealthStatus == health);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(project => project.Type == type);
        }

        if (query.ClientId is { } clientId)
        {
            rows = rows.Where(project => project.ClientId == clientId);
        }

        if (query.ManagerEmployeeId is { } managerId)
        {
            rows = rows.Where(project => project.ProjectManagerEmployeeId == managerId);
        }

        PagedResult<ProjectEntity> result = await rows.ApplySort(page.Sort, Sortable, "name").ToPagedResultAsync(page, project => project, cancellationToken);
        return PagedResult.From(await ToItemsAsync(result.Items, cancellationToken), page, result.TotalCount);
    }

    public async Task<ProjectDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ProjectEntity project = await access.LoadAsync(id, Permissions.Project.View, track: false, cancellationToken);
        return new ProjectDetail((await ToItemsAsync([project], cancellationToken))[0], await AbilitiesAsync(id, cancellationToken));
    }

    public async Task<ProjectAbilities> AbilitiesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        bool manage = await access.CanAsync(projectId, Permissions.Project.Manage, cancellationToken);
        return new ProjectAbilities(
            manage,
            await access.CanAsync(projectId, Permissions.Project.TaskManage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.MilestoneManage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.SprintManage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.RaidManage, cancellationToken),
            SeeRates: manage);
    }

    public async Task<ProjectDetail> CreateAsync(ProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string code = ProjectEntity.NormalizeCode(request.Code ?? string.Empty);

        if (await dbContext.Projects.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(project => project.Code == code, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_CODE_IN_USE", $"Project code '{code}' is already in use.");
        }

        // Whoever creates a project without naming a manager runs it - otherwise a project manager
        // whose reach is their own projects could create one they then could not open.
        ProjectRequest effective = request.ProjectManagerEmployeeId is null && access.Me is { } me
            ? request with { ProjectManagerEmployeeId = me }
            : request;

        ProjectProfile profile = await ValidateAsync(effective, existing: null, cancellationToken);
        var project = ProjectEntity.Create(code, profile, request.Stage ?? ProjectStage.Planning);
        dbContext.Projects.Add(project);

        int order = 0;
        foreach ((string name, ColumnCategory category) in BoardColumn.Defaults)
        {
            dbContext.BoardColumns.Add(BoardColumn.Create(project.Id, name, category, order++, null));
        }

        dbContext.ProjectStatusHistory.Add(ProjectStatusHistory.Record(project.Id, "Stage", "-", project.Stage.ToString(), "Project created", access.Me, Now));
        await dbContext.SaveChangesAsync(cancellationToken);
        access.Reset();
        return await GetAsync(project.Id, cancellationToken);
    }

    public async Task<ProjectDetail> UpdateAsync(Guid id, ProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectEntity project = await access.LoadAsync(id, Permissions.Project.Manage, track: true, cancellationToken);
        project.Update(await ValidateAsync(request, project, cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        access.Reset();
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProjectDetail> MoveToStageAsync(Guid id, StageBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectEntity project = await access.LoadAsync(id, Permissions.Project.Manage, track: true, cancellationToken);
        ProjectStage previous = project.MoveToStage(body.Stage);
        if (previous != body.Stage)
        {
            dbContext.ProjectStatusHistory.Add(ProjectStatusHistory.Record(id, "Stage", previous.ToString(), body.Stage.ToString(), body.Comment, access.Me, Now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProjectDetail> SetStatusAsync(Guid id, StatusBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectEntity project = await access.LoadAsync(id, Permissions.Project.Manage, track: true, cancellationToken);
        ProjectStatus previous = project.SetStatus(body.Status, await clock.TodayAsync(cancellationToken));
        if (previous != body.Status)
        {
            dbContext.ProjectStatusHistory.Add(ProjectStatusHistory.Record(id, "Status", previous.ToString(), body.Status.ToString(), body.Comment, access.Me, Now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task RateClientSatisfactionAsync(Guid id, SatisfactionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectEntity project = await access.LoadAsync(id, Permissions.Project.Manage, track: true, cancellationToken);
        byte? previous = project.ClientSatisfaction;
        project.RateClientSatisfaction(body.Score, await clock.TodayAsync(cancellationToken));
        dbContext.ProjectStatusHistory.Add(ProjectStatusHistory.Record(
            id, "Satisfaction", previous?.ToString(CultureInfo.InvariantCulture) ?? "-", body.Score.ToString(CultureInfo.InvariantCulture), body.Comment, access.Me, Now));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(id, Permissions.Project.View, cancellationToken);
        List<ProjectStatusHistory> rows = await dbContext.ProjectStatusHistory.AsNoTracking()
            .Where(row => row.ProjectId == id)
            .OrderByDescending(row => row.ChangedDate)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(rows.Select(row => row.ChangedByEmployeeId), cancellationToken);
        return [.. rows.Select(row => new HistoryItem(
            row.Id, row.Kind, row.FromValue, row.ToValue, row.Comment, row.ChangedByEmployeeId,
            row.ChangedByEmployeeId is { } by ? names.GetValueOrDefault(by) : null, row.ChangedDate))];
    }

    public async Task<PortfolioSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.View, cancellationToken);
        IQueryable<ProjectEntity> projects = dbContext.Projects.AsNoTracking();
        if (visible is not null)
        {
            projects = projects.Where(project => visible.Contains(project.Id));
        }

        var rows = await projects.Select(project => new { project.Id, project.Status, project.Stage, project.HealthStatus }).ToListAsync(cancellationToken);
        List<Guid> open = [.. rows.Where(row => row.Status is ProjectStatus.Active or ProjectStatus.OnHold).Select(row => row.Id)];
        DateOnly today = await clock.TodayAsync(cancellationToken);

        int overdueMilestones = await dbContext.ProjectMilestones.AsNoTracking()
            .CountAsync(milestone => open.Contains(milestone.ProjectId) && (milestone.Status == MilestoneStatus.Planned || milestone.Status == MilestoneStatus.InProgress) && milestone.PlannedDate < today, cancellationToken);
        int highRisks = await dbContext.RaidItems.AsNoTracking()
            .CountAsync(item => open.Contains(item.ProjectId) && item.Kind == RaidKind.Risk && (item.Status == RaidStatus.Open || item.Status == RaidStatus.InProgress) && item.Score >= 15, cancellationToken);
        int pendingChanges = await dbContext.ChangeRequests.AsNoTracking()
            .CountAsync(request => open.Contains(request.ProjectId) && request.Status == ChangeRequestStatus.PendingApproval, cancellationToken);

        var openRows = rows.Where(row => open.Contains(row.Id)).ToList();
        return new PortfolioSummary(
            rows.Count,
            rows.GroupBy(row => row.Status.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            openRows.GroupBy(row => row.Stage.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            openRows.GroupBy(row => row.HealthStatus.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            overdueMilestones,
            highRisks,
            pendingChanges);
    }

    // ----- Team ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TeamMemberItem>> TeamAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.View, cancellationToken);
        bool seeRates = await access.CanAsync(projectId, Permissions.Project.Manage, cancellationToken);
        List<ProjectTeamMember> members = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.ProjectId == projectId)
            .OrderBy(member => member.Status)
            .ThenBy(member => member.StartDate)
            .ToListAsync(cancellationToken);
        return await ToTeamItemsAsync(members, seeRates, cancellationToken);
    }

    public async Task<TeamMemberItem> AddMemberAsync(Guid projectId, TeamMemberBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectEntity project = await access.LoadAsync(projectId, Permissions.Project.Manage, track: false, cancellationToken);
        ProjectRole role = await ActiveRoleAsync(body.ProjectRoleId, cancellationToken);

        EmployeeSummary person = await employees.GetAsync(body.EmployeeId, cancellationToken)
            ?? throw new DomainRuleException("PROJECT_TEAM_EMPLOYEE", "Choose an employee for the team.");
        if (!await employees.IsActiveAsync(body.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TEAM_EMPLOYEE", $"{person.DisplayName} is not an active employee.");
        }

        if (await dbContext.ProjectTeamMembers.AnyAsync(
                member => member.ProjectId == projectId && member.EmployeeId == body.EmployeeId && member.ProjectRoleId == role.Id && member.Status == TeamMemberStatus.Active,
                cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TEAM_DUPLICATE", $"{person.DisplayName} is already on the team as {role.Name}.");
        }

        var member = ProjectTeamMember.Add(projectId, body.EmployeeId, ToAssignment(body));
        dbContext.ProjectTeamMembers.Add(member);
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(projectId, member.Id, member.EmployeeId, "Added"));

        if (body.EmployeeId != access.Me)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ProjectTeamAdded,
                    [new NotificationRecipient(body.EmployeeId)],
                    new Dictionary<string, string>
                    {
                        ["projectName"] = project.Name,
                        ["roleName"] = role.Name,
                        ["startDate"] = body.StartDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                    },
                    $"/projects/{projectId}"),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        access.Reset();
        return (await ToTeamItemsAsync([member], seeRates: true, cancellationToken))[0];
    }

    public async Task<TeamMemberItem> UpdateMemberAsync(Guid projectId, Guid memberId, TeamMemberBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        ProjectTeamMember member = await FindMemberAsync(projectId, memberId, cancellationToken);
        if (member.Status == TeamMemberStatus.Released)
        {
            throw new DomainRuleException("PROJECT_TEAM_RELEASED", "This person has been released from the project; add them again instead.");
        }

        await ActiveRoleAsync(body.ProjectRoleId, cancellationToken);
        member.Update(ToAssignment(body));
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(projectId, member.Id, member.EmployeeId, "Updated"));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToTeamItemsAsync([member], seeRates: true, cancellationToken))[0];
    }

    public async Task ReleaseMemberAsync(Guid projectId, Guid memberId, DateOnly on, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        ProjectTeamMember member = await FindMemberAsync(projectId, memberId, cancellationToken);
        member.Release(on);
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(projectId, member.Id, member.EmployeeId, "Released"));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Takes someone who has left the company off every project, as of their exit date.</summary>
    public async Task ReleaseEverywhereAsync(Guid employeeId, DateOnly on, CancellationToken cancellationToken)
    {
        List<ProjectTeamMember> seats = await dbContext.ProjectTeamMembers
            .Where(member => member.EmployeeId == employeeId && member.Status == TeamMemberStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (ProjectTeamMember seat in seats)
        {
            seat.Release(on);
            publisher.Publish(new ProjectTeamChangedIntegrationEvent(seat.ProjectId, seat.Id, seat.EmployeeId, "Released"));
        }
    }

    // ----- Phases -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PhaseItem>> PhasesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.View, cancellationToken);
        List<ProjectPhase> phases = await dbContext.ProjectPhases.AsNoTracking()
            .Where(phase => phase.ProjectId == projectId)
            .OrderBy(phase => phase.SortOrder).ThenBy(phase => phase.StartDate)
            .ToListAsync(cancellationToken);

        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId && task.PhaseId != null)
            .Select(task => new { task.PhaseId, task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> owners = await NamesAsync(phases.Select(phase => phase.OwnerEmployeeId), cancellationToken);

        return [.. phases.Select(phase =>
        {
            var mine = tasks.Where(task => task.PhaseId == phase.Id).ToList();
            (decimal measured, _) = ProgressMeter.Of(mine.Select(task => (task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Done)));
            return new PhaseItem(
                phase.Id, phase.Name, phase.SortOrder, phase.StartDate, phase.EndDate, phase.OwnerEmployeeId,
                phase.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null, phase.Budget, phase.Status, phase.Progress,
                mine.Count == 0 ? phase.Progress : measured, mine.Count);
        })];
    }

    public async Task<IReadOnlyList<PhaseItem>> SavePhaseAsync(Guid projectId, Guid? phaseId, PhaseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        await EnsureEmployeesAsync([body.OwnerEmployeeId], cancellationToken);

        if (phaseId is { } id)
        {
            ProjectPhase phase = await dbContext.ProjectPhases.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectPhase), id);
            phase.Update(ToPlan(body, body.SortOrder ?? phase.SortOrder));
        }
        else
        {
            int next = (await dbContext.ProjectPhases.Where(row => row.ProjectId == projectId).MaxAsync(row => (int?)row.SortOrder, cancellationToken) ?? -1) + 1;
            dbContext.ProjectPhases.Add(ProjectPhase.Create(projectId, ToPlan(body, body.SortOrder ?? next)));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PhasesAsync(projectId, cancellationToken);
    }

    public async Task DeletePhaseAsync(Guid projectId, Guid phaseId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        ProjectPhase phase = await dbContext.ProjectPhases.FirstOrDefaultAsync(row => row.Id == phaseId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectPhase), phaseId);

        if (await dbContext.ProjectTasks.AnyAsync(task => task.PhaseId == phaseId, cancellationToken)
            || await dbContext.ProjectMilestones.AnyAsync(milestone => milestone.PhaseId == phaseId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_PHASE_IN_USE", "Tasks or milestones still sit in this phase; move them first.");
        }

        dbContext.ProjectPhases.Remove(phase);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Board columns ------------------------------------------------------------------------

    public async Task<IReadOnlyList<ColumnItem>> ColumnsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.View, cancellationToken);
        List<BoardColumn> columns = await dbContext.BoardColumns.AsNoTracking().Where(column => column.ProjectId == projectId).OrderBy(column => column.SortOrder).ToListAsync(cancellationToken);
        Dictionary<Guid, int> counts = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId)
            .GroupBy(task => task.ColumnId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        return [.. columns.Select(column => new ColumnItem(column.Id, column.Name, column.SortOrder, column.Category, column.WipLimit, counts.GetValueOrDefault(column.Id)))];
    }

    /// <summary>
    /// Replaces the board's columns with <paramref name="columns"/>, in that order. Columns left out are
    /// removed, which only works once they are empty; a board always keeps a done column and one before it.
    /// </summary>
    public async Task<IReadOnlyList<ColumnItem>> SaveColumnsAsync(Guid projectId, IReadOnlyList<ColumnBody> columns, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(columns);
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);

        if (columns.Count is 0 or > 20 || !columns.Any(column => column.Category == ColumnCategory.Done) || columns.All(column => column.Category == ColumnCategory.Done))
        {
            throw new DomainRuleException("PROJECT_BOARD_SHAPE", "A board needs between 2 and 20 columns, at least one of them done and one not.");
        }

        if (columns.Select(column => column.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            throw new DomainRuleException("PROJECT_BOARD_NAMES", "Two columns have the same name.");
        }

        List<BoardColumn> existing = await dbContext.BoardColumns.Where(column => column.ProjectId == projectId).ToListAsync(cancellationToken);
        HashSet<Guid> kept = [.. columns.Select(column => column.Id).OfType<Guid>()];

        foreach (BoardColumn removed in existing.Where(column => !kept.Contains(column.Id)))
        {
            if (await dbContext.ProjectTasks.AnyAsync(task => task.ColumnId == removed.Id, cancellationToken))
            {
                throw new DomainRuleException("PROJECT_COLUMN_IN_USE", $"'{removed.Name}' still holds tasks; move them before removing the column.");
            }

            dbContext.BoardColumns.Remove(removed);
        }

        for (int index = 0; index < columns.Count; index++)
        {
            ColumnBody body = columns[index];
            if (body.Id is { } id)
            {
                BoardColumn column = existing.FirstOrDefault(row => row.Id == id) ?? throw new NotFoundException(nameof(BoardColumn), id);
                bool categoryChanged = column.Category != body.Category;
                column.Update(body.Name, body.Category, index, body.WipLimit);

                if (categoryChanged)
                {
                    // Tasks in a column that has become (or stopped being) "done" follow its meaning.
                    List<ProjectTask> held = await dbContext.ProjectTasks.Where(task => task.ColumnId == id).ToListAsync(cancellationToken);
                    foreach (ProjectTask task in held)
                    {
                        task.MoveTo(column, task.Rank, Now);
                    }
                }
            }
            else
            {
                dbContext.BoardColumns.Add(BoardColumn.Create(projectId, body.Name, body.Category, index, body.WipLimit));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ColumnsAsync(projectId, cancellationToken);
    }

    // ----- Roles and settings -------------------------------------------------------------------

    public async Task<IReadOnlyList<RoleItem>> RolesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        List<ProjectRole> roles = await dbContext.ProjectRoles.AsNoTracking()
            .Where(role => includeInactive || role.IsActive)
            .OrderBy(role => role.SortOrder).ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);
        return [.. roles.Select(ToRoleItem)];
    }

    public async Task<RoleItem> SaveRoleAsync(Guid? id, RoleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectRole role;
        if (id is { } roleId)
        {
            role = await dbContext.ProjectRoles.FirstOrDefaultAsync(row => row.Id == roleId, cancellationToken) ?? throw new NotFoundException(nameof(ProjectRole), roleId);
            role.Update(body.Name, body.Description, body.Skills ?? [], body.SortOrder ?? role.SortOrder);
        }
        else
        {
            string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (await dbContext.ProjectRoles.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("PROJECT_ROLE_CODE_IN_USE", $"A project role with code '{code}' already exists.");
            }

            int next = (await dbContext.ProjectRoles.MaxAsync(row => (int?)row.SortOrder, cancellationToken) ?? 0) + 10;
            role = ProjectRole.Create(code, body.Name, body.Description, body.Skills ?? [], body.SortOrder ?? next, isSystem: false);
            dbContext.ProjectRoles.Add(role);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRoleItem(role);
    }

    public async Task SetRoleActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        ProjectRole role = await dbContext.ProjectRoles.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ProjectRole), id);
        if (active)
        {
            role.Activate();
        }
        else
        {
            role.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectHealthThresholds> ThresholdsAsync(CancellationToken cancellationToken)
        => (await dbContext.ProjectHealthSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToThresholds() ?? ProjectHealthThresholds.Default;

    public async Task<ProjectHealthThresholds> SaveThresholdsAsync(ProjectHealthThresholds thresholds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ProjectHealthSettings? settings = await dbContext.ProjectHealthSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = ProjectHealthSettings.Defaults();
            dbContext.ProjectHealthSettings.Add(settings);
        }

        settings.Update(thresholds);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings.ToThresholds();
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<ProjectProfile> ValidateAsync(ProjectRequest request, ProjectEntity? existing, CancellationToken cancellationToken)
    {
        if (request.ClientId is { } clientId)
        {
            ClientSummary client = await clients.GetAsync(clientId, cancellationToken)
                ?? throw new DomainRuleException("PROJECT_CLIENT_UNKNOWN", "The client does not exist.");
            if (!client.CanTakeWork && existing?.ClientId != clientId)
            {
                throw new DomainRuleException("PROJECT_CLIENT_INACTIVE", $"{client.Name} is inactive and takes no new projects.");
            }
        }

        if (request.ContractId is { } contractId)
        {
            ContractSummary contract = (await contracts.GetManyAsync([contractId], cancellationToken)).GetValueOrDefault(contractId)
                ?? throw new DomainRuleException("PROJECT_CONTRACT_UNKNOWN", "The contract does not exist.");
            if (contract.ClientId != request.ClientId)
            {
                throw new DomainRuleException("PROJECT_CONTRACT_CLIENT", $"Contract {contract.Number} belongs to another client.");
            }

            if (!contract.IsLive && existing?.ContractId != contractId)
            {
                throw new DomainRuleException("PROJECT_CONTRACT_ENDED", $"Contract {contract.Number} is {contract.Status} and takes no new projects.");
            }
        }

        List<OrgReference> references = [];
        if (request.BusinessUnitId is { } unit)
        {
            references.Add(new OrgReference(OrgUnitKind.BusinessUnit, unit));
        }

        if (request.DepartmentId is { } department)
        {
            references.Add(new OrgReference(OrgUnitKind.Department, department));
        }

        if (request.LocationId is { } location)
        {
            references.Add(new OrgReference(OrgUnitKind.Location, location));
        }

        if ((await orgStructure.FindInvalidAsync(references, cancellationToken)).Count > 0)
        {
            throw new DomainRuleException("PROJECT_ORG_INVALID", "The business unit, department or location does not exist or is inactive.");
        }

        await EnsureEmployeesAsync([request.ProjectManagerEmployeeId, request.DeliveryManagerEmployeeId, request.AccountManagerEmployeeId], cancellationToken);

        if (!await masterData.IsValidAsync(LookupLists.Currency, request.Currency ?? string.Empty, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_CURRENCY_INVALID", $"'{request.Currency}' is not a currency this organization uses.");
        }

        if (!string.IsNullOrWhiteSpace(request.WorkModeCode) && !await masterData.IsValidAsync(LookupLists.WorkMode, request.WorkModeCode, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_WORK_MODE_INVALID", $"'{request.WorkModeCode}' is not a work mode this organization uses.");
        }

        return new ProjectProfile(
            request.Name, request.Type, request.Category, request.ClientId, request.ContractId, request.BusinessUnitId, request.DepartmentId,
            request.ProjectManagerEmployeeId, request.DeliveryManagerEmployeeId, request.AccountManagerEmployeeId, request.StartDate, request.EndDate,
            request.PlannedEndDate, request.Priority, request.BillingType, request.Currency ?? string.Empty, request.Budget, request.RevenueTarget,
            request.CostBudget, request.Description, request.LocationId, request.WorkModeCode, request.Confidentiality, request.Methodology);
    }

    internal async Task EnsureEmployeesAsync(IEnumerable<Guid?> employeeIds, CancellationToken cancellationToken)
    {
        foreach (Guid id in employeeIds.OfType<Guid>().Distinct())
        {
            if (!await employees.IsActiveAsync(id, cancellationToken))
            {
                throw new DomainRuleException("PROJECT_EMPLOYEE_INVALID", "Everyone named on a project must be an active employee.");
            }
        }
    }

    internal async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> employeeIds, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(employeeIds.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private async Task<ProjectRole> ActiveRoleAsync(Guid roleId, CancellationToken cancellationToken)
        => await dbContext.ProjectRoles.AsNoTracking().FirstOrDefaultAsync(role => role.Id == roleId && role.IsActive, cancellationToken)
            ?? throw new DomainRuleException("PROJECT_ROLE_UNKNOWN", "Choose an active project role.");

    private async Task<ProjectTeamMember> FindMemberAsync(Guid projectId, Guid memberId, CancellationToken cancellationToken)
        => await dbContext.ProjectTeamMembers.FirstOrDefaultAsync(member => member.Id == memberId && member.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectTeamMember), memberId);

    private static TeamAssignment ToAssignment(TeamMemberBody body)
        => new(body.ProjectRoleId, body.StartDate, body.EndDate, body.AllocationPercent, body.Billable, body.BillingRate, body.CostRate, body.Notes);

    private static PhasePlan ToPlan(PhaseBody body, int sortOrder)
        => new(body.Name, sortOrder, body.StartDate, body.EndDate, body.OwnerEmployeeId, body.Budget, body.Status, body.Progress);

    private static RoleItem ToRoleItem(ProjectRole role)
        => new(role.Id, role.Code, role.Name, role.Description, role.Skills, role.IsSystem, role.IsActive, role.SortOrder);

    private async Task<List<TeamMemberItem>> ToTeamItemsAsync(IReadOnlyList<ProjectTeamMember> members, bool seeRates, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> people = [.. members.Select(member => member.EmployeeId).Distinct()];
        Dictionary<Guid, EmployeeSummary> summaries = (await employees.GetManyAsync(people, cancellationToken)).ToDictionary(person => person.Id);
        List<Guid> roleIds = [.. members.Select(member => member.ProjectRoleId).Distinct()];
        Dictionary<Guid, string> roles = await dbContext.ProjectRoles.AsNoTracking().Where(role => roleIds.Contains(role.Id)).ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

        // Everyone's current load across all projects, so an over-allocation shows where it is made.
        Dictionary<Guid, int> load = (await dbContext.ProjectTeamMembers.AsNoTracking()
                .Where(member => people.Contains(member.EmployeeId) && member.Status == TeamMemberStatus.Active && member.StartDate <= today && (member.EndDate == null || member.EndDate >= today))
                .Select(member => new { member.EmployeeId, member.AllocationPercent })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.AllocationPercent));

        return [.. members.Select(member =>
        {
            EmployeeSummary? person = summaries.GetValueOrDefault(member.EmployeeId);
            int total = load.GetValueOrDefault(member.EmployeeId);
            return new TeamMemberItem(
                member.Id, member.EmployeeId, person?.DisplayName, person?.EmployeeCode, person?.Designation, member.ProjectRoleId,
                roles.GetValueOrDefault(member.ProjectRoleId), member.StartDate, member.EndDate, member.AllocationPercent, member.Billable,
                seeRates ? member.BillingRate : null, seeRates ? member.CostRate : null, member.Status, member.Notes, total, total > 100);
        })];
    }

    private async Task<List<ProjectItem>> ToItemsAsync(IReadOnlyList<ProjectEntity> projects, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. projects.Select(project => project.Id)];
        IReadOnlyDictionary<Guid, ClientSummary> clientNames = await clients.GetManyAsync(projects.Select(project => project.ClientId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, ContractSummary> contractNames = await contracts.GetManyAsync(projects.Select(project => project.ContractId).OfType<Guid>(), cancellationToken);
        Dictionary<Guid, string> people = await NamesAsync(
            projects.SelectMany(project => new[] { project.ProjectManagerEmployeeId, project.DeliveryManagerEmployeeId, project.AccountManagerEmployeeId }), cancellationToken);
        IReadOnlyDictionary<Guid, string> units = await orgStructure.GetNamesAsync(OrgUnitKind.BusinessUnit, projects.Select(project => project.BusinessUnitId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await orgStructure.GetNamesAsync(OrgUnitKind.Department, projects.Select(project => project.DepartmentId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await orgStructure.GetNamesAsync(OrgUnitKind.Location, projects.Select(project => project.LocationId).OfType<Guid>(), cancellationToken);

        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => ids.Contains(task.ProjectId))
            .Select(task => new { task.ProjectId, task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, int> teamSizes = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => ids.Contains(member.ProjectId) && member.Status == TeamMemberStatus.Active)
            .GroupBy(member => member.ProjectId)
            .Select(group => new { group.Key, Count = group.Select(member => member.EmployeeId).Distinct().Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        string? Name(Guid? id) => id is { } value ? people.GetValueOrDefault(value) : null;

        return [.. projects.Select(project =>
        {
            var mine = tasks.Where(task => task.ProjectId == project.Id).ToList();
            (decimal progress, _) = ProgressMeter.Of(mine.Select(task => (task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Done)));
            return new ProjectItem(
                project.Id, project.Code, project.Name, project.Type, project.Category, project.ClientId,
                project.ClientId is { } client ? clientNames.GetValueOrDefault(client)?.Name : null, project.ContractId,
                project.ContractId is { } contract ? contractNames.GetValueOrDefault(contract)?.Number : null,
                project.BusinessUnitId, project.BusinessUnitId is { } unit ? units.GetValueOrDefault(unit) : null,
                project.DepartmentId, project.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
                project.ProjectManagerEmployeeId, Name(project.ProjectManagerEmployeeId),
                project.DeliveryManagerEmployeeId, Name(project.DeliveryManagerEmployeeId),
                project.AccountManagerEmployeeId, Name(project.AccountManagerEmployeeId),
                project.StartDate, project.EndDate, project.PlannedEndDate, project.ActualEndDate, project.Priority, project.Status, project.Stage,
                project.HealthStatus, project.HealthAssessedOn, project.BillingType, project.Currency, project.Budget, project.RevenueTarget, project.CostBudget,
                project.Description, project.LocationId, project.LocationId is { } location ? locations.GetValueOrDefault(location) : null,
                project.WorkModeCode, project.Confidentiality, project.Methodology, project.ClientSatisfaction, progress,
                teamSizes.GetValueOrDefault(project.Id), mine.Count(task => !task.Done && task.Type != TaskType.Epic));
        })];
    }
}
