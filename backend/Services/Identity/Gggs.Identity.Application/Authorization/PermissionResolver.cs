using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Domain.Roles;

namespace Gggs.Identity.Application.Authorization;

/// <summary>
/// Works out what a user may actually do: their active role grants, the permissions those roles
/// carry, and how far each one reaches.
/// <para>
/// This is the calculation the whole authorization model rests on, so it is a pure function over
/// data the caller supplies rather than something that queries as it goes. It can then be tested
/// exhaustively without a database, which matters more here than anywhere else in the platform.
/// </para>
/// </summary>
public static class PermissionResolver
{
    /// <summary>
    /// Combines role grants into one permission set.
    /// <para>
    /// Where the same permission arrives through several roles, the widest reach wins. That is the
    /// intuitive reading of holding two roles - a team lead who is also an HR manager expects the
    /// HR manager's reach, not the narrower one - and the alternative would make adding a role
    /// sometimes reduce what someone can see.
    /// </para>
    /// </summary>
    public static PermissionSet Resolve(
        IEnumerable<UserRole> grants,
        IReadOnlyDictionary<Guid, Role> rolesById,
        DateOnly today,
        int permissionVersion)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(rolesById);

        Dictionary<string, PermissionGrant> effective = new(StringComparer.OrdinalIgnoreCase);

        foreach (UserRole grant in grants)
        {
            // Expired and not-yet-started grants are skipped rather than filtered by the caller,
            // so a query that forgets the date range cannot quietly restore lapsed access.
            if (!grant.IsActiveOn(today))
            {
                continue;
            }

            if (!rolesById.TryGetValue(grant.RoleId, out Role? role))
            {
                continue;
            }

            foreach (PermissionAssignment assignment in role.Permissions)
            {
                DataScope scope = grant.NarrowScope(assignment.Scope);

                if (effective.TryGetValue(assignment.PermissionCode, out PermissionGrant? existing)
                    && existing.Scope >= scope)
                {
                    continue;
                }

                effective[assignment.PermissionCode] = new PermissionGrant
                {
                    Code = assignment.PermissionCode,
                    Scope = scope,
                    ScopeId = grant.ScopeId,
                };
            }
        }

        return new PermissionSet(effective.Values, permissionVersion);
    }
}
