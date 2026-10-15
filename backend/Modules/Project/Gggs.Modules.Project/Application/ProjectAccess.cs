using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

/// <summary>
/// Which projects the caller reaches, and what they may do inside each.
/// <para>
/// A grant at Tenant scope reaches every project. Any narrower grant reaches the projects the caller
/// is on: as a team member (past or present) or as the project, delivery or account manager. A
/// project outside the caller's reach answers 404, as though it did not exist; one they can see but
/// may not change answers 403.
/// </para>
/// </summary>
internal sealed class ProjectAccess(AppDbContext dbContext, IPermissionProvider permissions, ICurrentUser currentUser)
{
    private readonly Dictionary<Guid, bool> _onProject = [];
    private HashSet<Guid>? _mine;

    public Guid? Me => currentUser.EmployeeId;

    /// <summary>Whether the caller's grant of <paramref name="permission"/> reaches every project.</summary>
    public async Task<bool> IsTenantWideAsync(string permission, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).ScopeFor(permission) == DataScope.Tenant;

    public async Task<bool> HasAsync(string permission, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(permission);

    /// <summary>The projects visible under <paramref name="permission"/>: null for all of them.</summary>
    public async Task<IReadOnlyCollection<Guid>?> VisibleIdsAsync(string permission, CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);
        DataScope? scope = held.ScopeFor(permission);
        if (scope == DataScope.Tenant)
        {
            return null;
        }

        return scope is null ? [] : await MineAsync(cancellationToken);
    }

    /// <summary>Narrows a project query to what the caller may see under <paramref name="permission"/>.</summary>
    public async Task<IQueryable<ProjectEntity>> ApplyAsync(IQueryable<ProjectEntity> query, string permission, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? visible = await VisibleIdsAsync(permission, cancellationToken);
        return visible is null ? query : query.Where(project => visible.Contains(project.Id));
    }

    /// <summary>
    /// Throws unless the caller may act on <paramref name="projectId"/> under <paramref name="permission"/>:
    /// 404 when they cannot see the project at all, 403 when they can see it but not do this.
    /// </summary>
    public async Task EnsureAsync(Guid projectId, string permission, CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

        if (!await dbContext.Projects.AnyAsync(project => project.Id == projectId, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        bool canView = held.ScopeFor(Permissions.Project.View) == DataScope.Tenant
            || (held.Has(Permissions.Project.View) && await IsOnProjectAsync(projectId, cancellationToken));
        if (!canView)
        {
            throw new NotFoundException("Project", projectId);
        }

        if (permission == Permissions.Project.View)
        {
            return;
        }

        DataScope? scope = held.ScopeFor(permission);
        if (scope == DataScope.Tenant || (scope is not null && await IsOnProjectAsync(projectId, cancellationToken)))
        {
            return;
        }

        throw new ForbiddenException("PROJECT_FORBIDDEN", "You can see this project but not make that change to it.");
    }

    /// <summary>Whether the caller may do <paramref name="permission"/> on the project, without throwing.</summary>
    public async Task<bool> CanAsync(Guid projectId, string permission, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureAsync(projectId, permission, cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    /// <summary>Loads a project the caller may act on under <paramref name="permission"/>.</summary>
    public async Task<ProjectEntity> LoadAsync(Guid projectId, string permission, bool track, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, permission, cancellationToken);
        IQueryable<ProjectEntity> source = track ? dbContext.Projects : dbContext.Projects.AsNoTracking();
        return await source.FirstAsync(project => project.Id == projectId, cancellationToken);
    }

    public async Task<bool> IsOnProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (_onProject.TryGetValue(projectId, out bool known))
        {
            return known;
        }

        bool on = (await MineAsync(cancellationToken)).Contains(projectId);
        _onProject[projectId] = on;
        return on;
    }

    /// <summary>The projects the caller manages or has been a team member of.</summary>
    private async Task<HashSet<Guid>> MineAsync(CancellationToken cancellationToken)
    {
        if (_mine is not null)
        {
            return _mine;
        }

        if (currentUser.EmployeeId is not { } me)
        {
            return _mine = [];
        }

        List<Guid> managed = await dbContext.Projects.AsNoTracking()
            .Where(project => project.ProjectManagerEmployeeId == me || project.DeliveryManagerEmployeeId == me || project.AccountManagerEmployeeId == me)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);
        List<Guid> member = await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(row => row.EmployeeId == me)
            .Select(row => row.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return _mine = [.. managed, .. member];
    }

    /// <summary>Forget what was worked out: the caller has just been put on (or taken off) a project.</summary>
    public void Reset()
    {
        _mine = null;
        _onProject.Clear();
    }

    /// <summary>Raised to 403 when an employee-less account tries to act as a team member.</summary>
    public Guid RequireEmployee()
        => currentUser.EmployeeId ?? throw new DomainRuleException("PROJECT_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    /// <summary>Statuses of team rows that count as being on the project now.</summary>
    public static bool IsActive(ProjectTeamMember member) => member.Status == TeamMemberStatus.Active;
}
