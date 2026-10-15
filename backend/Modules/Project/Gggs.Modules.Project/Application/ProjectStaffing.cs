using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

/// <summary>
/// Allocation commands for resource management: the same team rows the project screens edit, so
/// there is one allocation of record whichever door it came through.
/// </summary>
internal sealed class ProjectStaffing(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ITenantClock clock) : IProjectStaffing
{
    /// <summary>The role used when a request names none: the catalog's catch-all.</summary>
    private const string FallbackRoleCode = "CONSULTANT";

    public async Task<Guid> AllocateAsync(StaffingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Domain.Project project = await dbContext.Projects.AsNoTracking().FirstOrDefaultAsync(row => row.Id == request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        if (!project.IsOpen)
        {
            throw new DomainRuleException("PROJECT_CLOSED", $"{project.Code} is {project.Status} and takes no new allocations.");
        }

        if (!await employees.IsActiveAsync(request.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TEAM_EMPLOYEE", "Only active employees can be allocated.");
        }

        ProjectRole role = await RoleAsync(request.ProjectRoleId, cancellationToken);
        await TeamSeats.RetireLapsedAsync(dbContext, project.Id, request.EmployeeId, role.Id, await clock.TodayAsync(cancellationToken), cancellationToken);
        if (await dbContext.ProjectTeamMembers.AnyAsync(
                member => member.ProjectId == project.Id && member.EmployeeId == request.EmployeeId && member.ProjectRoleId == role.Id && member.Status == TeamMemberStatus.Active,
                cancellationToken))
        {
            throw new DomainRuleException("PROJECT_TEAM_DUPLICATE", $"This person is already on {project.Code} as {role.Name}.");
        }

        var member = ProjectTeamMember.Add(project.Id, request.EmployeeId, new TeamAssignment(
            role.Id, request.StartDate, request.EndDate, request.AllocationPercent, request.Billable, request.BillingRate, request.CostRate, request.Notes));
        dbContext.ProjectTeamMembers.Add(member);
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(project.Id, member.Id, member.EmployeeId, "Added"));

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.ProjectTeamAdded,
                [new NotificationRecipient(request.EmployeeId)],
                new Dictionary<string, string>
                {
                    ["projectName"] = project.Name,
                    ["roleName"] = role.Name,
                    ["startDate"] = request.StartDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                },
                $"/projects/{project.Id}"),
            cancellationToken);

        return member.Id;
    }

    public async Task UpdateAllocationAsync(Guid memberId, int allocationPercent, DateOnly startDate, DateOnly? endDate, bool billable, CancellationToken cancellationToken = default)
    {
        ProjectTeamMember member = await FindAsync(memberId, cancellationToken);
        if (member.Status == TeamMemberStatus.Released)
        {
            throw new DomainRuleException("PROJECT_TEAM_RELEASED", "This allocation has ended; allocate the person again instead.");
        }

        member.Update(new TeamAssignment(member.ProjectRoleId, startDate, endDate, allocationPercent, billable, member.BillingRate, member.CostRate, member.Notes));
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(member.ProjectId, member.Id, member.EmployeeId, "Updated"));
    }

    public async Task ReleaseAsync(Guid memberId, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        ProjectTeamMember member = await FindAsync(memberId, cancellationToken);
        member.Release(lastDay, await clock.TodayAsync(cancellationToken));
        publisher.Publish(new ProjectTeamChangedIntegrationEvent(member.ProjectId, member.Id, member.EmployeeId, "Released"));
    }

    private async Task<ProjectRole> RoleAsync(Guid? roleId, CancellationToken cancellationToken)
    {
        if (roleId is { } id)
        {
            return await dbContext.ProjectRoles.AsNoTracking().FirstOrDefaultAsync(role => role.Id == id && role.IsActive, cancellationToken)
                ?? throw new DomainRuleException("PROJECT_ROLE_UNKNOWN", "Choose an active project role.");
        }

        return await dbContext.ProjectRoles.AsNoTracking().Where(role => role.IsActive).OrderBy(role => role.Code == FallbackRoleCode ? 0 : 1).ThenBy(role => role.SortOrder).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("PROJECT_ROLE_UNKNOWN", "No project roles are set up.");
    }

    private async Task<ProjectTeamMember> FindAsync(Guid memberId, CancellationToken cancellationToken)
        => await dbContext.ProjectTeamMembers.FirstOrDefaultAsync(member => member.Id == memberId, cancellationToken)
            ?? throw new NotFoundException("Allocation", memberId);
}

/// <summary>Seats whose end date has passed are closed before the same person takes the same role again.</summary>
internal static class TeamSeats
{
    public static async Task RetireLapsedAsync(AppDbContext dbContext, Guid projectId, Guid employeeId, Guid roleId, DateOnly today, CancellationToken cancellationToken)
    {
        List<ProjectTeamMember> lapsed = await dbContext.ProjectTeamMembers
            .Where(member => member.ProjectId == projectId && member.EmployeeId == employeeId && member.ProjectRoleId == roleId
                && member.Status == TeamMemberStatus.Active && member.EndDate != null && member.EndDate < today)
            .ToListAsync(cancellationToken);
        foreach (ProjectTeamMember member in lapsed)
        {
            member.Release(member.EndDate!.Value, today);
        }

        if (lapsed.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Project access for other modules, decided by <see cref="ProjectAccess"/>.</summary>
internal sealed class ProjectPermissions(ProjectAccess access) : IProjectPermissions
{
    public Task<bool> CanAsync(Guid projectId, string permission, CancellationToken cancellationToken = default)
        => access.CanAsync(projectId, permission, cancellationToken);

    public Task<IReadOnlyCollection<Guid>?> VisibleIdsAsync(string permission, CancellationToken cancellationToken = default)
        => access.VisibleIdsAsync(permission, cancellationToken);
}
