using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authorization;
using Gggs.Modules.Identity.Domain.Roles;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// The calculation the whole authorization model rests on. It is a pure function over role grants,
/// so it can be tested exhaustively without a database - which matters more here than anywhere
/// else, because getting it wrong either exposes data or blocks someone from doing their job.
/// </summary>
public sealed class PermissionResolverTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    [Fact]
    public void AUserWithNoRoles_HoldsNothing()
    {
        PermissionSet permissions = PermissionResolver.Resolve([], new Dictionary<Guid, Role>(), Today, 1);

        permissions.Count.ShouldBe(0);
    }

    [Fact]
    public void PermissionsComeFromTheRolesGranted()
    {
        Role hrManager = RoleWith("HR Manager", (Permissions.Employee.View, DataScope.Branch));

        PermissionSet permissions = Resolve(hrManager, UserRole.Grant(UserId, hrManager.Id, Today));

        permissions.Has(Permissions.Employee.View).ShouldBeTrue();
        permissions.ScopeFor(Permissions.Employee.View).ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void HoldingTwoRoles_GivesTheWiderReach()
    {
        // A team lead who is also an HR manager expects the HR manager's reach. The alternative
        // would mean adding a role sometimes reduces what someone can see.
        Role teamLead = RoleWith("Team Lead", (Permissions.Leave.RequestView, DataScope.DirectReports));
        Role hrManager = RoleWith("HR Manager", (Permissions.Leave.RequestView, DataScope.Branch));

        PermissionSet permissions = PermissionResolver.Resolve(
            [UserRole.Grant(UserId, teamLead.Id, Today), UserRole.Grant(UserId, hrManager.Id, Today)],
            new Dictionary<Guid, Role> { [teamLead.Id] = teamLead, [hrManager.Id] = hrManager },
            Today,
            1);

        permissions.ScopeFor(Permissions.Leave.RequestView).ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void TheWiderReachWins_WhicheverOrderTheRolesArriveIn()
    {
        // Resolution must not depend on the order rows come back from the database.
        Role teamLead = RoleWith("Team Lead", (Permissions.Leave.RequestView, DataScope.DirectReports));
        Role hrManager = RoleWith("HR Manager", (Permissions.Leave.RequestView, DataScope.Branch));

        PermissionSet permissions = PermissionResolver.Resolve(
            [UserRole.Grant(UserId, hrManager.Id, Today), UserRole.Grant(UserId, teamLead.Id, Today)],
            new Dictionary<Guid, Role> { [teamLead.Id] = teamLead, [hrManager.Id] = hrManager },
            Today,
            1);

        permissions.ScopeFor(Permissions.Leave.RequestView).ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void AnExpiredGrantContributesNothing()
    {
        // The auditor whose access ended last month holds nothing today, even though the row is
        // still there for the audit trail.
        Role auditor = RoleWith("Auditor", (Permissions.Audit.LogView, DataScope.Tenant));

        PermissionSet permissions = Resolve(
            auditor,
            UserRole.Grant(UserId, auditor.Id, Today.AddDays(-60), Today.AddDays(-1), reason: "Year-end audit"));

        permissions.Has(Permissions.Audit.LogView).ShouldBeFalse();
    }

    [Fact]
    public void AGrantThatStartsNextWeek_ContributesNothingYet()
    {
        Role hrManager = RoleWith("HR Manager", (Permissions.Employee.View, DataScope.Branch));

        PermissionSet permissions = Resolve(hrManager, UserRole.Grant(UserId, hrManager.Id, Today.AddDays(7)));

        permissions.Has(Permissions.Employee.View).ShouldBeFalse();
    }

    [Fact]
    public void AnAnchoredGrantNarrowsWhatTheRoleWouldOtherwiseAllow()
    {
        // The same HR Manager role, granted for one branch, does not reach the whole tenant.
        Role hrManager = RoleWith("HR Manager", (Permissions.Employee.View, DataScope.Tenant));

        PermissionSet permissions = Resolve(
            hrManager,
            UserRole.Grant(UserId, hrManager.Id, Today, scopeType: RoleScopeType.Branch, scopeId: Guid.CreateVersion7()));

        permissions.ScopeFor(Permissions.Employee.View).ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void AGrantReferencingAMissingRole_IsIgnoredRatherThanFailing()
    {
        // A dangling grant should not take down authorization for every other role the user holds.
        Role hrManager = RoleWith("HR Manager", (Permissions.Employee.View, DataScope.Branch));

        PermissionSet permissions = PermissionResolver.Resolve(
            [
                UserRole.Grant(UserId, hrManager.Id, Today),
                UserRole.Grant(UserId, Guid.CreateVersion7(), Today),
            ],
            new Dictionary<Guid, Role> { [hrManager.Id] = hrManager },
            Today,
            1);

        permissions.Has(Permissions.Employee.View).ShouldBeTrue();
        permissions.Count.ShouldBe(1);
    }

    [Fact]
    public void ThePermissionVersionIsCarriedThrough()
    {
        // The cache is keyed by it, so losing it here would make role revocation take effect only
        // when the entry happened to expire.
        Role role = RoleWith("Employee", (Permissions.Self.ProfileView, DataScope.Own));

        PermissionSet permissions = PermissionResolver.Resolve(
            [UserRole.Grant(UserId, role.Id, Today)],
            new Dictionary<Guid, Role> { [role.Id] = role },
            Today,
            permissionVersion: 7);

        permissions.PermissionVersion.ShouldBe(7);
    }

    private static PermissionSet Resolve(Role role, UserRole grant)
        => PermissionResolver.Resolve([grant], new Dictionary<Guid, Role> { [role.Id] = role }, Today, 1);

    private static Role RoleWith(string name, params (string Code, DataScope Scope)[] permissions)
    {
        Role role = Role.Create(name, null);

        foreach ((string code, DataScope scope) in permissions)
        {
            role.Grant(code, scope);
        }

        return role;
    }
}
