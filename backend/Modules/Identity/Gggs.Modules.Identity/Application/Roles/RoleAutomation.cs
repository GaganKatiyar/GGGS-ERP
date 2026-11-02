using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Organization.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Application.Roles;

/// <summary>
/// Keeps automatic role grants in step with the org chart for the current tenant: managers hold the
/// people-manager roles, department heads the department-head roles, and nobody keeps one the org
/// chart no longer gives them. Grants made by hand are never added to or ended here.
/// </summary>
public sealed class RoleAutomation(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    TimeProvider timeProvider,
    ILogger<RoleAutomation> logger)
{
    /// <summary>Brings the tenant's automatic grants up to date. Returns how many grants changed.</summary>
    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        List<AutomaticRole> roles = await dbContext.Roles.AsNoTracking()
            .Where(role => role.AutoAssign != RoleAutoAssign.None)
            .Select(role => new AutomaticRole(role.Id, role.AutoAssign))
            .ToListAsync(cancellationToken);

        // Runs on every employee event, so a tenant with nothing automatic costs one small query.
        if (roles.Count == 0 && !await dbContext.UserRoles.AnyAsync(grant => grant.IsAutomatic && (grant.ValidTo == null || grant.ValidTo >= today), cancellationToken))
        {
            return 0;
        }

        // Only the grants the plan can touch or must respect: automatic ones, and any grant (by hand
        // included) of a role that is assigned automatically. Tracked: the plan may end some of these.
        List<Guid> automaticRoleIds = [.. roles.Select(role => role.RoleId)];
        List<UserRole> grants = await dbContext.UserRoles
            .Where(grant => (grant.IsAutomatic || automaticRoleIds.Contains(grant.RoleId)) && (grant.ValidTo == null || grant.ValidTo >= today))
            .ToListAsync(cancellationToken);
        List<UserRole> inForce = [.. grants.Where(grant => grant.IsActiveOn(today))];

        List<EmployeeLink> links = await dbContext.Users.AsNoTracking()
            .Where(user => user.EmployeeId != null)
            .Select(user => new EmployeeLink(user.Id, user.EmployeeId!.Value))
            .ToListAsync(cancellationToken);

        IReadOnlySet<Guid> managers = roles.Any(role => role.AutoAssign == RoleAutoAssign.PeopleManagers)
            ? await employees.PeopleManagersAsync(cancellationToken)
            : new HashSet<Guid>();
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> heads = roles.Any(role => role.AutoAssign == RoleAutoAssign.DepartmentHeads)
            ? await orgStructure.DepartmentHeadsAsync(cancellationToken)
            : new Dictionary<Guid, IReadOnlyList<Guid>>();

        RoleAutomationChanges changes = RoleAutomationPlan.Work(
            roles,
            links,
            managers,
            heads,
            [.. inForce.Select(grant => new ActiveGrant(grant.Id, grant.UserId, grant.RoleId, grant.ScopeType, grant.ScopeId, grant.IsAutomatic))]);

        if (changes.IsEmpty)
        {
            return 0;
        }

        foreach (WantedGrant want in changes.ToGrant)
        {
            dbContext.UserRoles.Add(UserRole.GrantAutomatically(want.UserId, want.RoleId, today, want.DepartmentId));
        }

        foreach (UserRole grant in grants.Where(grant => changes.ToEnd.Contains(grant.Id)))
        {
            // The access goes now, not at midnight. A grant that only began today has no history worth
            // keeping, and cannot end before it began.
            if (grant.ValidFrom >= today)
            {
                dbContext.UserRoles.Remove(grant);
            }
            else
            {
                grant.EndOn(today.AddDays(-1));
            }
        }

        // Without this a signed-in user's cached permissions would keep the old roles until they expired.
        HashSet<Guid> changedUsers =
        [
            .. changes.ToGrant.Select(want => want.UserId),
            .. grants.Where(grant => changes.ToEnd.Contains(grant.Id)).Select(grant => grant.UserId),
        ];
        foreach (User user in await dbContext.Users.Where(user => changedUsers.Contains(user.Id)).ToListAsync(cancellationToken))
        {
            user.BumpPermissionVersion();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two syncs for the same tenant raced (a hire and a manager change arriving together) and
            // the other one wrote first. Its result stands; the nightly sync settles anything left.
            logger.LogWarning(exception, "Automatic role sync lost a race with another sync; leaving its result in place.");
            return 0;
        }

        logger.LogInformation(
            "Automatic roles: {Granted} granted, {Ended} ended, for {Users} user(s).",
            changes.ToGrant.Count,
            changes.ToEnd.Count,
            changedUsers.Count);

        return changes.ToGrant.Count + changes.ToEnd.Count;
    }
}

/// <summary>Someone joined, possibly reporting to a manager who has had nobody until now.</summary>
internal sealed class SyncRolesOnHire(RoleAutomation automation) : IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => automation.SyncAsync(cancellationToken);
}

/// <summary>A new manager, a transfer or a promotion can make or end someone's reporting line.</summary>
internal sealed class SyncRolesOnAssignmentChange(RoleAutomation automation) : IIntegrationEventConsumer<EmployeeAssignmentChangedIntegrationEvent>
{
    public Task HandleAsync(EmployeeAssignmentChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => automation.SyncAsync(cancellationToken);
}

/// <summary>A leaver neither manages anyone nor heads anything any more.</summary>
internal sealed class SyncRolesOnExit(RoleAutomation automation) : IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => automation.SyncAsync(cancellationToken);
}

internal sealed class SyncRolesOnDepartmentHeadChange(RoleAutomation automation) : IIntegrationEventConsumer<DepartmentHeadChangedIntegrationEvent>
{
    public Task HandleAsync(DepartmentHeadChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => automation.SyncAsync(cancellationToken);
}

/// <summary>Every night, queues one automatic-role sync per active tenant: the safety net under the events.</summary>
[PlatformJob("Role automation: fans out one automatic-role sync per active tenant.")]
internal sealed class RoleAutomationSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "role-automation";

    public Task RunAsync() => fanOut.FanOutAsync<RoleAutomationJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class RoleAutomationJob(RoleAutomation automation, ILogger<RoleAutomationJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int changed = await automation.SyncAsync(CancellationToken.None);
        logger.LogInformation("Automatic role sync for tenant {TenantId}: {Changed} grant(s) changed.", tenantId, changed);
    }
}

internal sealed class RoleAutomationJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<RoleAutomationSweep>(RoleAutomationSweep.RecurringJobName, sweep => sweep.RunAsync(), "40 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
