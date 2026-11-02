using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authorization;
using Gggs.Modules.Identity.Domain.Roles;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>"Why can this person do that": the effective permissions, each with the roles behind it.</summary>
public sealed class PermissionSourcesTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");

    [Fact]
    public void EachPermission_NamesEveryRoleThatGivesIt_WithTheReachTheResolverEnforces()
    {
        Role teamLead = RoleWith("Team Lead", (Permissions.Leave.RequestView, DataScope.DirectReports));
        Role hrManager = RoleWith("HR Manager", (Permissions.Leave.RequestView, DataScope.Branch), (Permissions.Employee.View, DataScope.Branch));

        IReadOnlyList<PermissionExplanation> explained = PermissionSources.Explain(
            [UserRole.Grant(UserId, teamLead.Id, Today), UserRole.Grant(UserId, hrManager.Id, Today)],
            new Dictionary<Guid, Role> { [teamLead.Id] = teamLead, [hrManager.Id] = hrManager },
            Today);

        PermissionExplanation leave = explained.Single(permission => permission.Code == Permissions.Leave.RequestView);
        leave.Scope.ShouldBe(DataScope.Branch, "the wider reach wins, as it does when requests are authorized");
        leave.ViaRoleIds.ShouldBe([teamLead.Id, hrManager.Id], ignoreOrder: true);

        explained.Single(permission => permission.Code == Permissions.Employee.View).ViaRoleIds.ShouldBe([hrManager.Id]);
    }

    [Fact]
    public void AnEndedGrant_ExplainsNothing()
    {
        Role auditor = RoleWith("Auditor", (Permissions.Audit.LogView, DataScope.Tenant));
        UserRole ended = UserRole.Grant(UserId, auditor.Id, Today.AddDays(-30), Today.AddDays(-1), reason: "Year-end audit");

        PermissionSources.Explain([ended], new Dictionary<Guid, Role> { [auditor.Id] = auditor }, Today).ShouldBeEmpty();
    }

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
