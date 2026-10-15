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
