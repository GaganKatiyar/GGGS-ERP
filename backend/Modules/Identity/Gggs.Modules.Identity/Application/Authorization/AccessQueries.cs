using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Application.Authorization;

/// <summary>A role grant as the access screens show it: whose, which role, anchored where, from when.</summary>
public sealed record AccessGrant(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    bool IsAutomatic,
    string? ScopeType,
    Guid? ScopeId,
    string? ScopeName,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

/// <summary>One account on the access overview.</summary>
public sealed record UserAccessSummary(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    Guid? EmployeeId,
    string? EmployeeName,
    DateTime? LastLoginDate,
    IReadOnlyList<AccessGrant> Roles,
    IReadOnlyList<string> PermissionCodes);

/// <summary>Someone holding a role, for the role's Members tab.</summary>
public sealed record RoleMemberItem(Guid UserId, string DisplayName, string Email, bool IsAutomatic, string? ScopeName, DateOnly ValidFrom, DateOnly? ValidTo);

public sealed record EffectivePermissionItem(string Code, string Scope, Guid? ScopeId, IReadOnlyList<string> Via, bool IsSensitive);

/// <summary>Everything one user may do and why.</summary>
public sealed record UserAccessDetail(UserAccessSummary User, IReadOnlyList<EffectivePermissionItem> Permissions);

/// <summary>
/// The read side of the access screens: who holds which role, and what that adds up to. Effective
/// permissions come from <see cref="PermissionResolver"/> (through <see cref="PermissionSources"/>),
/// the same calculation every request is authorized with.
/// </summary>
public sealed class AccessQueries(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    TimeProvider timeProvider)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<UserAccessSummary>> UsersAsync(CancellationToken cancellationToken = default)
    {
        List<User> users = await dbContext.Users.AsNoTracking().Where(user => user.UserType != UserType.ApiClient).OrderBy(user => user.DisplayName).ToListAsync(cancellationToken);
        return await SummariesAsync(users, cancellationToken);
    }

    public async Task<UserAccessDetail> UserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        UserAccessSummary summary = (await SummariesAsync([user], cancellationToken))[0];
        Dictionary<Guid, Role> roles = await dbContext.Roles.AsNoTracking().ToDictionaryAsync(role => role.Id, cancellationToken);
        List<UserRole> grants = await dbContext.UserRoles.AsNoTracking().Where(grant => grant.UserId == userId).ToListAsync(cancellationToken);

        List<EffectivePermissionItem> permissions =
        [
            .. PermissionSources.Explain(grants, roles, Today).Select(permission => new EffectivePermissionItem(
                permission.Code,
                permission.Scope.ToString(),
                permission.ScopeId,
                [.. permission.ViaRoleIds.Select(id => roles.TryGetValue(id, out Role? role) ? role.Name : string.Empty).Where(name => name.Length > 0)],
                PermissionCatalog.IsSensitive(permission.Code))),
        ];

        return new UserAccessDetail(summary, permissions);
    }

    /// <summary>Everyone holding the role today, by hand or from the org chart.</summary>
    public async Task<IReadOnlyList<RoleMemberItem>> MembersAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Roles.AnyAsync(role => role.Id == roleId, cancellationToken))
        {
            throw new NotFoundException(nameof(Role), roleId);
        }

        List<UserRole> grants = await dbContext.UserRoles.AsNoTracking().Where(grant => grant.RoleId == roleId).ToListAsync(cancellationToken);
        IReadOnlyList<AccessGrant> described = await DescribeAsync([.. grants.Where(grant => grant.IsActiveOn(Today))], cancellationToken);

        List<Guid> userIds = [.. described.Select(grant => grant.UserId).Distinct()];
        Dictionary<Guid, User> users = await dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);

        return
        [
            .. described
                .Where(grant => users.ContainsKey(grant.UserId))
                .Select(grant => new RoleMemberItem(grant.UserId, users[grant.UserId].DisplayName, users[grant.UserId].Email, grant.IsAutomatic, grant.ScopeName, grant.ValidFrom, grant.ValidTo))
                .OrderBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    private async Task<IReadOnlyList<UserAccessSummary>> SummariesAsync(List<User> users, CancellationToken cancellationToken)
    {
        List<Guid> userIds = [.. users.Select(user => user.Id)];
        List<UserRole> grants = await dbContext.UserRoles.AsNoTracking().Where(grant => userIds.Contains(grant.UserId)).ToListAsync(cancellationToken);
        grants = [.. grants.Where(grant => grant.IsActiveOn(Today))];

        Dictionary<Guid, Role> roles = await dbContext.Roles.AsNoTracking().ToDictionaryAsync(role => role.Id, cancellationToken);
        IReadOnlyList<AccessGrant> described = await DescribeAsync(grants, cancellationToken);

        Dictionary<Guid, string> employeeNames = (await employees.GetManyAsync(users.Where(user => user.EmployeeId is not null).Select(user => user.EmployeeId!.Value), cancellationToken))
            .ToDictionary(employee => employee.Id, employee => employee.DisplayName);

        return
        [
            .. users.Select(user => new UserAccessSummary(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Status.ToString(),
                user.EmployeeId,
                user.EmployeeId is { } employee ? employeeNames.GetValueOrDefault(employee) : null,
                user.LastLoginDate,
                [.. described.Where(grant => grant.UserId == user.Id).OrderBy(grant => grant.RoleName, StringComparer.CurrentCultureIgnoreCase)],
                [.. PermissionResolver.Resolve(grants.Where(grant => grant.UserId == user.Id), roles, Today, 0).All.Select(permission => permission.Code).Order(StringComparer.Ordinal)])),
        ];
    }

    private async Task<IReadOnlyList<AccessGrant>> DescribeAsync(List<UserRole> grants, CancellationToken cancellationToken)
    {
        List<Guid> roleIds = [.. grants.Select(grant => grant.RoleId).Distinct()];
        Dictionary<Guid, string> roleNames = await dbContext.Roles.AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

        var scopeNames = new Dictionary<Guid, string>();
        foreach (IGrouping<RoleScopeType, UserRole> anchored in grants.Where(grant => grant.ScopeId is not null && grant.ScopeType is not null and not RoleScopeType.Tenant).GroupBy(grant => grant.ScopeType!.Value))
        {
            OrgUnitKind kind = anchored.Key switch
            {
                RoleScopeType.LegalEntity => OrgUnitKind.LegalEntity,
                RoleScopeType.BusinessUnit => OrgUnitKind.BusinessUnit,
                RoleScopeType.Branch => OrgUnitKind.Location,
                _ => OrgUnitKind.Department,
            };

            foreach ((Guid id, string name) in await orgStructure.GetNamesAsync(kind, anchored.Select(grant => grant.ScopeId!.Value).Distinct(), cancellationToken))
            {
                scopeNames[id] = name;
            }
        }

        return
        [
            .. grants.Select(grant => new AccessGrant(
                grant.UserId,
                grant.RoleId,
                roleNames.GetValueOrDefault(grant.RoleId) ?? string.Empty,
                grant.IsAutomatic,
                grant.ScopeType?.ToString(),
                grant.ScopeId,
                grant.ScopeId is { } scope ? scopeNames.GetValueOrDefault(scope) : null,
                grant.ValidFrom,
                grant.ValidTo)),
        ];
    }
}
