using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Domain.PlatformStaff;

namespace Gggs.Modules.Identity.Application.PlatformStaff;

/// <summary>
/// Works out what a platform staff member may do: the union of permission codes across every role
/// they hold.
/// <para>
/// The tenant equivalent (<c>PermissionResolver</c>) also resolves a <see cref="DataScope"/> per
/// permission and narrows it by an anchor and a date range, because a tenant role can be scoped to
/// a branch and a grant can be temporary. Neither concept exists here: platform permissions are not
/// anchored to any part of an organization - there is no organization above a tenant to anchor
/// to - and platform access is not time-boxed today. What is left is simpler than the tenant
/// version on purpose, not a cut corner.
/// </para>
/// </summary>
public static class PlatformPermissionResolver
{
    /// <summary>
    /// The scope recorded on every platform grant.
    /// <para>
    /// <see cref="PermissionGrant"/> requires one because it is a tenant-shaped type; nothing that
    /// reads a platform permission consults it (<c>PermissionAuthorizationHandler</c> only checks
    /// whether the code is held), so <see cref="DataScope.Tenant"/> - the widest value the enum
    /// defines - is used as a fixed placeholder rather than adding a value to a widely-shared enum
    /// for a field nothing downstream reads.
    /// </para>
    /// </summary>
    private const DataScope PlaceholderScope = DataScope.Tenant;

    public static PermissionSet Resolve(
        IEnumerable<PlatformRoleAssignment> grants,
        IReadOnlyDictionary<Guid, PlatformRole> rolesById,
        int permissionVersion)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(rolesById);

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PlatformRoleAssignment grant in grants)
        {
            if (!rolesById.TryGetValue(grant.PlatformRoleId, out PlatformRole? role))
            {
                continue;
            }

            foreach (string code in role.PermissionCodes)
            {
                codes.Add(code);
            }
        }

        return new PermissionSet(
            codes.Select(code => new PermissionGrant { Code = code, Scope = PlaceholderScope }),
            permissionVersion);
    }
}
