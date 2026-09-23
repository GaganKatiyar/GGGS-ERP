using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Domain.Roles;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>Role and grant rules: what a role may contain, and how far a grant of it reaches.</summary>
public sealed class RoleTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);

    [Fact]
    public void ARoleCanOnlyGrantPermissionsThePlatformDefines()
    {
        // A role naming a code no endpoint checks looks like access and grants nothing, which is
        // worse for the administrator than being refused outright.
        Role role = Role.Create("HR Manager", "Manages employees");

        Should.Throw<DomainRuleException>(() => role.Grant("leave.request.approv", DataScope.Branch))
            .ErrorCode.ShouldBe("PERMISSION_UNKNOWN");
    }

    [Fact]
    public void ATenantRoleCannotGrantAPlatformPermission()
    {
        // Otherwise a tenant administrator could grant themselves the customer list.
        Role role = Role.Create("Tenant Admin", "Administers this organization");

        Should.Throw<DomainRuleException>(() => role.Grant(Permissions.Platform.TenantManage, DataScope.Tenant))
            .ErrorCode.ShouldBe("PERMISSION_PLATFORM_ONLY");
    }

    [Fact]
    public void GrantingTheSamePermissionTwice_ReplacesTheScope()
    {
        Role role = Role.Create("HR Manager", null);

        role.Grant(Permissions.Employee.View, DataScope.Department);
        role.Grant(Permissions.Employee.View, DataScope.Branch);

        role.Permissions.ShouldHaveSingleItem().Scope.ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void ASystemRoleCannotBeDeleted()
    {
        // A tenant that deletes "Employee" has no way to grant anyone self-service again.
        Role role = Role.Create("Employee", "Self-service", isSystem: true, templateCode: "EMPLOYEE");

        Should.Throw<DomainRuleException>(role.EnsureCanBeDeleted)
            .ErrorCode.ShouldBe("ROLE_IS_SYSTEM");
    }

    [Fact]
    public void ASystemRolesPermissionsCanStillBeChanged()
    {
        // Customers legitimately tailor what "HR Manager" means; they just cannot remove the role.
        Role role = Role.Create("HR Manager", null, isSystem: true, templateCode: "HR_MANAGER");

        role.Grant(Permissions.Employee.Edit, DataScope.Branch);

        role.Permissions.ShouldHaveSingleItem();
    }

    [Fact]
    public void AnAnchoredGrantNarrowsTheRolesReach()
    {
        // One "HR Manager" role serves a company with twelve branches: same permissions, each
        // holder seeing only their own branch.
        UserRole grant = UserRole.Grant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Today,
            scopeType: RoleScopeType.Branch,
            scopeId: Guid.CreateVersion7());

        grant.NarrowScope(DataScope.Tenant).ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void AnAnchoredGrantNeverWidensTheRolesReach()
    {
        // A role limited to direct reports stays limited, even anchored to a whole branch.
        UserRole grant = UserRole.Grant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Today,
            scopeType: RoleScopeType.Branch,
            scopeId: Guid.CreateVersion7());

        grant.NarrowScope(DataScope.DirectReports).ShouldBe(DataScope.DirectReports);
    }

    [Fact]
    public void AnAnchoredGrantMustNameItsAnchor()
    {
        // Without the anchor it would silently widen to the whole tenant - the opposite of intent.
        Should.Throw<DomainRuleException>(() => UserRole.Grant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Today,
            scopeType: RoleScopeType.Branch,
            scopeId: null))
            .ErrorCode.ShouldBe("USER_ROLE_SCOPE_MISSING");
    }

    [Fact]
    public void ATimeLimitedGrantMustRecordWhy()
    {
        // Temporary access nobody can explain later is an audit finding.
        Should.Throw<DomainRuleException>(() => UserRole.Grant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Today,
            validTo: Today.AddDays(30)))
            .ErrorCode.ShouldBe("USER_ROLE_REASON_REQUIRED");
    }

    [Fact]
    public void AnExpiredGrantStopsApplying()
    {
        // Access granted "just for a few weeks" is otherwise still there years later.
        UserRole grant = UserRole.Grant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Today.AddDays(-60),
            validTo: Today.AddDays(-1),
            reason: "Year-end audit");

        grant.IsActiveOn(Today).ShouldBeFalse();
        grant.IsActiveOn(Today.AddDays(-30)).ShouldBeTrue();
    }

    [Fact]
    public void AGrantThatHasNotStartedYet_IsNotActive()
    {
        UserRole grant = UserRole.Grant(Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(7));

        grant.IsActiveOn(Today).ShouldBeFalse();
    }

    [Fact]
    public void EndingAGrantKeepsItForTheAuditTrail()
    {
        // Deleted access cannot be explained afterwards; ended access can.
        UserRole grant = UserRole.Grant(Guid.CreateVersion7(), Guid.CreateVersion7(), Today.AddDays(-30));

        grant.EndOn(Today);

        grant.ValidTo.ShouldBe(Today);
        grant.IsActiveOn(Today.AddDays(1)).ShouldBeFalse();
    }
}
