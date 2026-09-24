using Gggs.Identity.Application.Users;
using Gggs.Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Identity.Infrastructure;

/// <summary>Role and grant persistence, scoped to the current tenant by the query filter.</summary>
public sealed class RoleRepository(IdentityDbContext dbContext, TimeProvider timeProvider) : IRoleRepository
{
    public async Task<Role?> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
        => await dbContext.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public async Task<IReadOnlyList<UserRole>> GetGrantsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await dbContext.UserRoles
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Everyone who currently holds a permission, through any role and any active grant.
    /// <para>
    /// Deliberately evaluated in memory rather than in SQL. Role permissions are stored as JSON,
    /// so the alternative is a JSON query that would need an index nobody would remember to add -
    /// and this runs only when disabling a user or clearing their roles, where a handful of roles
    /// is being scanned, not a table of them.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetUserIdsWithPermissionAsync(
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        List<Role> roles = await dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);

        List<Guid> roleIds =
        [
            .. roles
                .Where(role => role.Permissions.Any(permission =>
                    string.Equals(permission.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase)))
                .Select(role => role.Id)
        ];

        if (roleIds.Count == 0)
        {
            return [];
        }

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        List<UserRole> grants = await dbContext.UserRoles
            .AsNoTracking()
            .Where(grant => roleIds.Contains(grant.RoleId))
            .ToListAsync(cancellationToken);

        return
        [
            .. grants
                .Where(grant => grant.IsActiveOn(today))
                .Select(grant => grant.UserId)
                .Distinct()
        ];
    }

    public void Add(Role role) => dbContext.Roles.Add(role);

    public void AddGrant(UserRole grant) => dbContext.UserRoles.Add(grant);
}
