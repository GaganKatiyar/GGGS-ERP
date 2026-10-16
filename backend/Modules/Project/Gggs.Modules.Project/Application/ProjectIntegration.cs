using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

/// <summary>Project lookups for other modules.</summary>
internal sealed class ProjectDirectory(AppDbContext dbContext) : IProjectDirectory
{
    public async Task<IReadOnlyDictionary<Guid, ProjectSummary>> GetManyAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. projectIds.Distinct()];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ProjectSummary>();
        }

        List<Domain.Project> rows = await dbContext.Projects.AsNoTracking().Where(project => ids.Contains(project.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(
            project => project.Id,
            project => new ProjectSummary(
                project.Id, project.Code, project.Name, project.ClientId, project.Status.ToString(), project.Stage.ToString(),
                project.ProjectManagerEmployeeId, project.StartDate, project.EndDate));
    }

    public async Task<IReadOnlyList<ProjectMembership>> MembershipsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.EmployeeId == employeeId)
            .Join(dbContext.Projects.AsNoTracking(), member => member.ProjectId, project => project.Id, (member, project) => new { member, project })
            .Join(dbContext.ProjectRoles.AsNoTracking(), row => row.member.ProjectRoleId, role => role.Id, (row, role) => new { row.member, row.project, RoleName = role.Name })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(row => new ProjectMembership(
                row.member.Id, row.project.Id, row.project.Code, row.project.Name, row.project.Status.ToString(), row.member.EmployeeId, row.RoleName,
                row.member.AllocationPercent, row.member.StartDate, row.member.EndDate, row.member.Billable, row.member.Status == TeamMemberStatus.Active))
            .OrderByDescending(membership => membership.IsActive)
            .ThenByDescending(membership => membership.StartDate)];
    }

    public async Task<IReadOnlyList<ProjectAllocation>> AllocationsAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        IQueryable<ProjectTeamMember> members = dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.StartDate <= lastDay && (member.EndDate == null || member.EndDate >= firstDay));
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds.Distinct()];
            members = members.Where(member => ids.Contains(member.EmployeeId));
        }

        var rows = await members
            .Join(dbContext.Projects.AsNoTracking(), member => member.ProjectId, project => project.Id, (member, project) => new { member, project })
            .Join(dbContext.ProjectRoles.AsNoTracking(), row => row.member.ProjectRoleId, role => role.Id, (row, role) => new { row.member, row.project, RoleName = role.Name })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ProjectAllocation(
            row.member.Id, row.project.Id, row.project.Code, row.project.Name, row.project.Status.ToString(), row.member.EmployeeId, row.member.ProjectRoleId,
            row.RoleName, row.member.AllocationPercent, row.member.StartDate, row.member.EndDate, row.member.Billable, row.member.BillingRate,
            row.member.CostRate, row.member.Status == TeamMemberStatus.Active))];
    }

    public async Task<bool> IsOnTeamAsync(Guid projectId, Guid employeeId, DateOnly day, CancellationToken cancellationToken = default)
        => await dbContext.ProjectTeamMembers.AnyAsync(
                member => member.ProjectId == projectId && member.EmployeeId == employeeId && member.StartDate <= day && (member.EndDate == null || member.EndDate >= day),
                cancellationToken)
            || await dbContext.Projects.AnyAsync(
                project => project.Id == projectId
                    && (project.ProjectManagerEmployeeId == employeeId || project.DeliveryManagerEmployeeId == employeeId || project.AccountManagerEmployeeId == employeeId),
                cancellationToken);

    public async Task<IReadOnlyList<TimesheetTarget>> TimesheetTargetsAsync(Guid employeeId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        List<Guid> memberOf = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.EmployeeId == employeeId && member.StartDate <= lastDay && (member.EndDate == null || member.EndDate >= firstDay))
            .Select(member => member.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var billable = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => member.EmployeeId == employeeId && memberOf.Contains(member.ProjectId))
            .GroupBy(member => member.ProjectId)
            .Select(group => new { group.Key, Billable = group.Any(member => member.Billable) })
            .ToDictionaryAsync(row => row.Key, row => row.Billable, cancellationToken);

        List<Domain.Project> projects = await dbContext.Projects.AsNoTracking()
            .Where(project => memberOf.Contains(project.Id)
                || project.ProjectManagerEmployeeId == employeeId || project.DeliveryManagerEmployeeId == employeeId)
            .Where(project => project.Status == ProjectStatus.Active || project.Status == ProjectStatus.OnHold)
            .OrderBy(project => project.Code)
            .ToListAsync(cancellationToken);
        List<Guid> projectIds = [.. projects.Select(project => project.Id)];
        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => projectIds.Contains(task.ProjectId) && task.AssigneeEmployeeId == employeeId && task.Type != TaskType.Epic
                && (task.CompletedDate == null || task.CompletedDate >= firstDay.ToDateTime(TimeOnly.MinValue)))
            .OrderBy(task => task.Number)
            .Select(task => new { task.ProjectId, task.Id, task.Code, task.Title })
            .ToListAsync(cancellationToken);

        return [.. projects.Select(project => new TimesheetTarget(
            project.Id, project.Code, project.Name,
            billable.TryGetValue(project.Id, out bool isBillable) ? isBillable : project.BillingType != ProjectBillingType.NonBillable,
            [.. tasks.Where(task => task.ProjectId == project.Id).Select(task => new TimesheetTask(task.Id, task.Code, task.Title))]))];
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> TaskProjectsAsync(IEnumerable<Guid> taskIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. taskIds.Distinct()];
        return ids.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await dbContext.ProjectTasks.AsNoTracking().Where(task => ids.Contains(task.Id)).ToDictionaryAsync(task => task.Id, task => task.ProjectId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectFinancialFacts>> FinancialFactsAsync(IEnumerable<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.Project> query = dbContext.Projects.AsNoTracking();
        if (projectIds is not null)
        {
            List<Guid> wanted = [.. projectIds.Distinct()];
            query = query.Where(project => wanted.Contains(project.Id));
        }
        else
        {
            query = query.Where(project => project.Status != ProjectStatus.Cancelled);
        }

        List<Domain.Project> projects = await query.OrderBy(project => project.Code).ToListAsync(cancellationToken);
        List<Guid> ids = [.. projects.Select(project => project.Id)];
        if (ids.Count == 0)
        {
            return [];
        }

        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => ids.Contains(task.ProjectId))
            .Select(task => new { task.ProjectId, task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, Done = task.CompletedDate != null })
            .ToListAsync(cancellationToken);
        List<ProjectMilestone> milestones = await dbContext.ProjectMilestones.AsNoTracking()
            .Where(milestone => ids.Contains(milestone.ProjectId) && milestone.Status != MilestoneStatus.Cancelled)
            .OrderBy(milestone => milestone.PlannedDate)
            .ToListAsync(cancellationToken);
        var team = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(member => ids.Contains(member.ProjectId))
            .Join(dbContext.ProjectRoles.AsNoTracking(), member => member.ProjectRoleId, role => role.Id, (member, role) => new { member, RoleName = role.Name })
            .ToListAsync(cancellationToken);

        return [.. projects.Select(project =>
        {
            (decimal progress, _) = ProgressMeter.Of(tasks.Where(task => task.ProjectId == project.Id).Select(task => (task.Type, task.StoryPoints, task.EstimatedHours, task.Progress, task.Done)));
            return new ProjectFinancialFacts(
                project.Id, project.Code, project.Name, project.Status.ToString(), project.BillingType.ToString(), project.Currency, project.Budget,
                project.RevenueTarget, project.CostBudget, project.ClientId, project.ContractId, project.ProjectManagerEmployeeId, project.DepartmentId,
                project.StartDate, project.EndDate, progress,
                [.. milestones.Where(milestone => milestone.ProjectId == project.Id).Select(milestone => new MilestoneFact(
                    milestone.Id, milestone.Name, milestone.PlannedDate, milestone.ActualDate, milestone.Status.ToString(), milestone.IsBillable))],
                [.. team.Where(row => row.member.ProjectId == project.Id).Select(row => new ProjectAllocation(
                    row.member.Id, project.Id, project.Code, project.Name, project.Status.ToString(), row.member.EmployeeId, row.member.ProjectRoleId,
                    row.RoleName, row.member.AllocationPercent, row.member.StartDate, row.member.EndDate, row.member.Billable, row.member.BillingRate,
                    row.member.CostRate, row.member.Status == TeamMemberStatus.Active))]);
        })];
    }
}

/// <summary>Project documents (spec §55) follow the project: the team reads them, those who run the project add them.</summary>
internal sealed class ProjectDocumentAccess(AppDbContext dbContext, ProjectAccess access) : IDocumentOwnerAccess
{
    public string OwnerType => "Project";

    public Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => access.CanAsync(ownerId, Permissions.Project.View, cancellationToken);

    public async Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await access.CanAsync(ownerId, Permissions.Project.Manage, cancellationToken)
            || await access.CanAsync(ownerId, Permissions.Project.TaskManage, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. ownerIds.Distinct()];
        return await dbContext.Projects.AsNoTracking().Where(project => ids.Contains(project.Id)).ToDictionaryAsync(project => project.Id, project => $"{project.Code} {project.Name}", cancellationToken);
    }
}

/// <summary>The project roles a new tenant starts with (spec §42); editable after.</summary>
internal static class ProjectCatalog
{
    public static IReadOnlyList<(string Code, string Name, string[] Skills)> Roles { get; } =
    [
        ("PM", "Project Manager", ["Project planning", "Stakeholder management", "Risk management"]),
        ("PGM", "Program Manager", ["Program governance", "Portfolio planning"]),
        ("DM", "Delivery Manager", ["Delivery governance", "Client management"]),
        ("ARCH", "Architect", ["Solution architecture", "System design"]),
        ("TL", "Tech Lead", ["Code review", "Technical design"]),
        ("DEV", "Developer", ["Software development"]),
        ("QA", "QA Engineer", ["Test design", "Test automation"]),
        ("BA", "Business Analyst", ["Requirements analysis", "Process modelling"]),
        ("SM", "Scrum Master", ["Scrum", "Facilitation"]),
        ("DEVOPS", "DevOps Engineer", ["CI/CD", "Cloud infrastructure"]),
        ("UX", "UI/UX Designer", ["User research", "Interaction design"]),
        ("DE", "Data Engineer", ["Data pipelines", "SQL"]),
        ("AI", "AI Engineer", ["Machine learning", "LLM applications"]),
        ("CONSULTANT", "Consultant", ["Consulting"]),
    ];
}

/// <summary>Gives the current tenant every catalog project role it lacks, and its health thresholds row.</summary>
public sealed class ProjectCatalogSeeder(AppDbContext dbContext, ILogger<ProjectCatalogSeeder> logger)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.ProjectRoles
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(role => role.Code)
            .ToListAsync(cancellationToken)];

        int added = 0;
        int order = 10;
        foreach ((string code, string name, string[] skills) in ProjectCatalog.Roles)
        {
            if (!codes.Contains(code))
            {
                dbContext.ProjectRoles.Add(ProjectRole.Create(code, name, null, skills, order, isSystem: true));
                added++;
            }

            order += 10;
        }

        if (!await dbContext.ProjectHealthSettings.AnyAsync(cancellationToken))
        {
            dbContext.ProjectHealthSettings.Add(ProjectHealthSettings.Defaults());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            logger.LogInformation("Project catalog seeded: {Added} role(s) added.", added);
        }

        return added;
    }
}

internal sealed class SeedProjectCatalogOnTenantProvisioned(ProjectCatalogSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>Someone who has left the company comes off every project team on their exit date.</summary>
internal sealed class ReleaseFromProjectsOnExit(ProjectService projects) : IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return projects.ReleaseEverywhereAsync(integrationEvent.EmployeeId, integrationEvent.ExitDate, cancellationToken);
    }
}

internal sealed class ApproveChangeOnApproval(GovernanceService governance) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == GovernanceService.ChangeRequestSubjectType)
        {
            await governance.ApplyDecisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectChangeOnRejection(GovernanceService governance) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == GovernanceService.ChangeRequestSubjectType)
        {
            await governance.ApplyDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}

internal sealed class ReturnChangeOnSendBack(GovernanceService governance) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == GovernanceService.ChangeRequestSubjectType)
        {
            await governance.ApplyDecisionAsync(integrationEvent.SubjectId, approved: null, cancellationToken);
        }
    }
}

/// <summary>Approved timesheet hours become tasks' actual hours (spec §70: timesheets feed the project).</summary>
internal sealed class AddHoursOnTimesheetApproval(AppDbContext dbContext) : IIntegrationEventConsumer<TimesheetApprovedIntegrationEvent>
{
    public Task HandleAsync(TimesheetApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return TaskHoursLedger.ApplyAsync(dbContext, integrationEvent.TaskHours, sign: 1, cancellationToken);
    }
}

/// <summary>A reopened timesheet's hours come off its tasks until it is approved again.</summary>
internal sealed class RemoveHoursOnTimesheetReopened(AppDbContext dbContext) : IIntegrationEventConsumer<TimesheetReopenedIntegrationEvent>
{
    public Task HandleAsync(TimesheetReopenedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return TaskHoursLedger.ApplyAsync(dbContext, integrationEvent.TaskHours, sign: -1, cancellationToken);
    }
}

internal static class TaskHoursLedger
{
    public static async Task ApplyAsync(AppDbContext dbContext, IReadOnlyList<TaskHours> hours, int sign, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. hours.Select(row => row.TaskId)];
        if (ids.Count == 0)
        {
            return;
        }

        List<ProjectTask> tasks = await dbContext.ProjectTasks.Where(task => ids.Contains(task.Id)).ToListAsync(cancellationToken);
        foreach (ProjectTask task in tasks)
        {
            task.AddActualHours(sign * hours.Where(row => row.TaskId == task.Id).Sum(row => row.Hours));
        }
    }
}

/// <summary>Fans the nightly health assessment out to every tenant.</summary>
internal sealed class ProjectHealthSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "project-health";

    public Task RunAsync() => fanOut.FanOutAsync<ProjectHealthJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class ProjectHealthJob(InsightService insights, ILogger<ProjectHealthJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int assessed = await insights.AssessAllAsync(CancellationToken.None);
        if (assessed > 0)
        {
            logger.LogInformation("Project health for tenant {TenantId}: {Count} project(s) assessed.", tenantId, assessed);
        }
    }
}

internal sealed class ProjectJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<ProjectHealthSweep>(ProjectHealthSweep.RecurringJobName, sweep => sweep.RunAsync(), "45 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
