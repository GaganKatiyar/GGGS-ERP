using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Domain.Roles;

namespace Gggs.Modules.Identity.Application.Authorization;

/// <summary>One effective permission, and the roles it arrives through.</summary>
public sealed record PermissionExplanation(string Code, DataScope Scope, Guid? ScopeId, IReadOnlyList<Guid> ViaRoleIds);

/// <summary>
/// The "why can this person do that" companion to <see cref="PermissionResolver"/>: the same
/// effective set, with every role that contributes each permission. The reach is taken from the
/// resolver itself, so an access screen can never disagree with what is actually enforced.
/// </summary>
public static class PermissionSources
{
    public static IReadOnlyList<PermissionExplanation> Explain(
        IEnumerable<UserRole> grants,
        IReadOnlyDictionary<Guid, Role> rolesById,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(rolesById);

        List<UserRole> active = [.. grants.Where(grant => grant.IsActiveOn(today))];
        PermissionSet effective = PermissionResolver.Resolve(active, rolesById, today, permissionVersion: 0);

        var via = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (UserRole grant in active)
        {
            if (!rolesById.TryGetValue(grant.RoleId, out Role? role))
            {
                continue;
            }

            foreach (PermissionAssignment assignment in role.Permissions)
            {
                if (!via.TryGetValue(assignment.PermissionCode, out List<Guid>? roles))
                {
                    via[assignment.PermissionCode] = roles = [];
                }

                if (!roles.Contains(role.Id))
                {
                    roles.Add(role.Id);
                }
            }
        }

        return
        [
            .. effective.All
                .OrderBy(grant => grant.Code, StringComparer.Ordinal)
                .Select(grant => new PermissionExplanation(grant.Code, grant.Scope, grant.ScopeId, via.GetValueOrDefault(grant.Code) ?? [])),
        ];
    }
}
