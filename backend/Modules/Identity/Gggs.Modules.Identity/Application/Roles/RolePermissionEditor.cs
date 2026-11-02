using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Application.Roles;

/// <summary>A permission and its reach, as the role grid saves it.</summary>
public sealed record PermissionChoice(string PermissionCode, DataScope Scope);

/// <summary>
/// Changes what a role means. Every change reaches the people holding the role at once (their
/// permission version is bumped), and a change that would leave nobody able to manage users or
/// roles is refused - the tenant could not undo it.
/// </summary>
public sealed class RolePermissionEditor(
    AppDbContext dbContext,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RolePermissionEditor> logger)
{
    private static readonly (string Code, string Message)[] Keystones =
    [
        (Permissions.Admin.UserManage, "Nobody would be left who can manage users. Give another role that permission first."),
        (Permissions.Admin.RoleManage, "Nobody would be left who can change roles. Give another role that permission first."),
    ];

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Replaces the role's whole permission set, as the grid shows it after editing.</summary>
    public async Task<Role> ReplaceAsync(Guid roleId, IReadOnlyList<PermissionChoice> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        Role role = await dbContext.Roles.FirstOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        // The last choice for a code wins, so a grid that sends a row twice cannot fail on it.
        Dictionary<string, DataScope> wanted = new(StringComparer.OrdinalIgnoreCase);
        foreach (PermissionChoice choice in permissions)
        {
            wanted[Guard.NotNullOrWhiteSpace(choice.PermissionCode).Trim()] = choice.Scope;
        }

        await EnsureAdministratorsRemainAsync(role, wanted.Keys, cancellationToken);

        Dictionary<string, DataScope> before = role.Permissions.ToDictionary(permission => permission.PermissionCode, permission => permission.Scope, StringComparer.OrdinalIgnoreCase);
        foreach (string code in before.Keys.Where(code => !wanted.ContainsKey(code)))
        {
            role.Revoke(code);
        }

        // Role.Grant refuses unknown and platform-only codes, so a bad row fails the whole save.
        foreach ((string code, DataScope scope) in wanted)
        {
            role.Grant(code, scope);
        }

        await BumpHoldersAsync(role.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Role {RoleId} permissions replaced: {Added} added, {Removed} removed, {Changed} changed reach.",
            role.Id,
            wanted.Keys.Count(code => !before.ContainsKey(code)),
            before.Keys.Count(code => !wanted.ContainsKey(code)),
            wanted.Count(pair => before.TryGetValue(pair.Key, out DataScope old) && old != pair.Value));

        return role;
    }

    /// <summary>
    /// Makes a change to a role take effect for its holders now rather than when their cached
    /// permissions expire. Saved with the caller's unit of work.
    /// </summary>
    public async Task BumpHoldersAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        DateOnly today = Today;
        List<UserRole> grants = await dbContext.UserRoles.AsNoTracking().Where(grant => grant.RoleId == roleId).ToListAsync(cancellationToken);
        List<Guid> holders = [.. grants.Where(grant => grant.IsActiveOn(today)).Select(grant => grant.UserId).Distinct()];

        foreach (User user in await dbContext.Users.Where(user => holders.Contains(user.Id)).ToListAsync(cancellationToken))
        {
            user.BumpPermissionVersion();
        }
    }

    private async Task EnsureAdministratorsRemainAsync(Role role, IEnumerable<string> wantedCodes, CancellationToken cancellationToken)
    {
        HashSet<string> wanted = new(wantedCodes, StringComparer.OrdinalIgnoreCase);
        (string Code, string Message)[] lost = [.. Keystones.Where(keystone => role.Permissions.Any(permission => Same(permission.PermissionCode, keystone.Code)) && !wanted.Contains(keystone.Code))];
        if (lost.Length == 0)
        {
            return;
        }

        List<Role> roles = await dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        List<UserRole> grants = await dbContext.UserRoles.AsNoTracking().ToListAsync(cancellationToken);
        List<UserRole> active = [.. grants.Where(grant => grant.IsActiveOn(Today))];

        foreach ((string code, string message) in lost)
        {
            // The roles that would still carry the permission once this one no longer does.
            HashSet<Guid> carriers = [.. roles.Where(other => other.Id != role.Id && other.Permissions.Any(permission => Same(permission.PermissionCode, code))).Select(other => other.Id)];
            if (!active.Any(grant => carriers.Contains(grant.RoleId)))
            {
                throw new DomainRuleException("LAST_ADMINISTRATOR", message);
            }
        }
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
