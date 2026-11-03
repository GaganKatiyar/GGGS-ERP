using Gggs.Modules.Identity.Domain.Roles;

namespace Gggs.Modules.Identity.Application.Roles;

/// <summary>A role the org chart hands out, and to whom.</summary>
public sealed record AutomaticRole(Guid RoleId, RoleAutoAssign AutoAssign);

/// <summary>A user account and the employee it signs in as.</summary>
public sealed record EmployeeLink(Guid UserId, Guid EmployeeId);

/// <summary>A grant in force today, as the plan needs to see it.</summary>
public sealed record ActiveGrant(Guid GrantId, Guid UserId, Guid RoleId, RoleScopeType? ScopeType, Guid? ScopeId, bool IsAutomatic);

/// <summary>An automatic grant the org chart calls for: tenant-wide, or anchored to the department headed.</summary>
public sealed record WantedGrant(Guid UserId, Guid RoleId, Guid? DepartmentId);

public sealed record RoleAutomationChanges(IReadOnlyList<WantedGrant> ToGrant, IReadOnlyList<Guid> ToEnd)
{
    public bool IsEmpty => ToGrant.Count == 0 && ToEnd.Count == 0;
}

/// <summary>
/// What automatic role grants should change, worked out from the org chart and the grants in force.
/// Pure, so the rules are tested without a database:
/// <list type="bullet">
/// <item>a people-manager role goes, tenant-wide, to every account whose employee manages someone;</item>
/// <item>a department-head role goes to each department's head, anchored to that department;</item>
/// <item>a grant made by hand wins - no automatic copy is added beside one that already covers it;</item>
/// <item>automatic grants nobody calls for any more (the reports moved, the role was switched off) end.</item>
/// </list>
/// </summary>
public static class RoleAutomationPlan
{
    public static RoleAutomationChanges Work(
        IEnumerable<AutomaticRole> roles,
        IEnumerable<EmployeeLink> links,
        IReadOnlySet<Guid> peopleManagers,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> departmentHeads,
        IReadOnlyList<ActiveGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(peopleManagers);
        ArgumentNullException.ThrowIfNull(departmentHeads);
        ArgumentNullException.ThrowIfNull(grants);

        List<EmployeeLink> accounts = [.. links];
        var wanted = new HashSet<WantedGrant>();

        foreach (AutomaticRole role in roles)
        {
            foreach (EmployeeLink account in accounts)
            {
                if (role.AutoAssign == RoleAutoAssign.PeopleManagers && peopleManagers.Contains(account.EmployeeId))
                {
                    wanted.Add(new WantedGrant(account.UserId, role.RoleId, null));
                }
                else if (role.AutoAssign == RoleAutoAssign.DepartmentHeads && departmentHeads.TryGetValue(account.EmployeeId, out IReadOnlyList<Guid>? departments))
                {
                    foreach (Guid department in departments)
                    {
                        wanted.Add(new WantedGrant(account.UserId, role.RoleId, department));
                    }
                }
            }
        }

        List<ActiveGrant> byHand = [.. grants.Where(grant => !grant.IsAutomatic)];
        wanted.RemoveWhere(want => byHand.Any(grant => Covers(grant, want)));

        List<ActiveGrant> automatic = [.. grants.Where(grant => grant.IsAutomatic)];

        List<WantedGrant> toGrant =
        [
            .. wanted
                .Where(want => !automatic.Any(grant => Matches(grant, want)))
                .OrderBy(want => want.UserId)
                .ThenBy(want => want.RoleId),
        ];

        List<Guid> toEnd = [.. automatic.Where(grant => !wanted.Any(want => Matches(grant, want))).Select(grant => grant.GrantId)];

        return new RoleAutomationChanges(toGrant, toEnd);
    }

    /// <summary>A tenant-wide grant by hand covers any anchor; an anchored one covers its own department only.</summary>
    private static bool Covers(ActiveGrant grant, WantedGrant want)
        => grant.UserId == want.UserId
            && grant.RoleId == want.RoleId
            && (grant.ScopeType is null or RoleScopeType.Tenant
                || (grant.ScopeType == RoleScopeType.Department && grant.ScopeId == want.DepartmentId));

    private static bool Matches(ActiveGrant grant, WantedGrant want)
        => grant.UserId == want.UserId
            && grant.RoleId == want.RoleId
            && (want.DepartmentId is null
                ? grant.ScopeType is null or RoleScopeType.Tenant
                : grant.ScopeType == RoleScopeType.Department && grant.ScopeId == want.DepartmentId);
}
