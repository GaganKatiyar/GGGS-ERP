using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>Platform role rules - the mirror image of <see cref="RoleTests"/>.</summary>
public sealed class PlatformRoleTests
{
    [Fact]
    public void TheSeededRoles_OnlyTheStaffAdministratorManagesStaffOrRoles_AndBillingCannotCreateOrganisations()
    {
        // Whoever can change what a platform role means, or who holds one, should be a short list.
        foreach (Gggs.Modules.Identity.Application.PlatformStaff.PlatformRoleTemplate template in
                 Gggs.Modules.Identity.Application.PlatformStaff.PlatformRoleTemplates.Definitions)
        {
            bool isAdministrator = template.Code == Gggs.Modules.Identity.Application.PlatformStaff.PlatformRoleTemplates.StaffAdministratorCode;
            template.Permissions.Contains(Permissions.Platform.StaffManage).ShouldBe(isAdministrator, template.Code);
            template.Permissions.Contains(Permissions.Platform.RoleManage).ShouldBe(isAdministrator, template.Code);

            // Every template must be grantable: platform-only permissions, all known.
            PlatformRole role = PlatformRole.Create(template.Name, template.Description);
            foreach (string permission in template.Permissions)
            {
                role.Grant(permission);
            }
        }

        var billing = Gggs.Modules.Identity.Application.PlatformStaff.PlatformRoleTemplates.Definitions
            .Single(template => template.Code == Gggs.Modules.Identity.Application.PlatformStaff.PlatformRoleTemplates.BillingCode);
        billing.Permissions.ShouldContain(Permissions.Platform.SubscriptionManage);
        billing.Permissions.ShouldNotContain(Permissions.Platform.TenantManage);
    }

    [Fact]
    public void APlatformRoleCanOnlyGrantPermissionsThePlatformDefines()
    {
        PlatformRole role = PlatformRole.Create("Support", "Looks at tenant health");

        Should.Throw<DomainRuleException>(() => role.Grant("platform.tenant.viewww"))
            .ErrorCode.ShouldBe("PERMISSION_UNKNOWN");
    }

    [Fact]
    public void APlatformRoleCannotGrantATenantPermission()
    {
        // The mirror of the tenant check: a platform account granted a tenant permission could act
        // as if it worked for whichever tenant is asking, which is exactly the boundary the
        // isolation model exists to hold.
        PlatformRole role = PlatformRole.Create("Support", null);

        Should.Throw<DomainRuleException>(() => role.Grant(Permissions.Employee.View))
            .ErrorCode.ShouldBe("PERMISSION_NOT_PLATFORM_ONLY");
    }

    [Fact]
    public void GrantingThePlatformOnlyPermissionsSucceeds()
    {
        PlatformRole role = PlatformRole.Create("Support", null);

        role.Grant(Permissions.Platform.TenantView);
        role.Grant(Permissions.Platform.HealthView);

        role.PermissionCodes.ShouldBe([Permissions.Platform.TenantView, Permissions.Platform.HealthView], ignoreOrder: true);
    }

    [Fact]
    public void GrantingTheSamePermissionTwiceIsANoOp()
    {
        PlatformRole role = PlatformRole.Create("Support", null);

        role.Grant(Permissions.Platform.TenantView);
        role.Grant(Permissions.Platform.TenantView);

        role.PermissionCodes.ShouldHaveSingleItem();
    }

    [Fact]
    public void RevokingRemovesThePermission()
    {
        PlatformRole role = PlatformRole.Create("Support", null);
        role.Grant(Permissions.Platform.TenantView);

        role.Revoke(Permissions.Platform.TenantView);

        role.PermissionCodes.ShouldBeEmpty();
    }

    [Fact]
    public void ASystemRoleCannotBeDeleted()
    {
        PlatformRole role = PlatformRole.Create(
            "Platform Staff Administrator",
            "Full control",
            isSystem: true,
            templateCode: "PLATFORM_STAFF_ADMINISTRATOR");

        Should.Throw<DomainRuleException>(role.EnsureCanBeDeleted)
            .ErrorCode.ShouldBe("PLATFORM_ROLE_IS_SYSTEM");
    }

    [Fact]
    public void ASystemRoleCannotBeRenamed_ButItsPermissionsCanStillBeChanged()
    {
        PlatformRole role = PlatformRole.Create("Support", null, isSystem: true, templateCode: "PLATFORM_SUPPORT");

        Should.Throw<DomainRuleException>(() => role.Rename("New Name", null))
            .ErrorCode.ShouldBe("PLATFORM_ROLE_IS_SYSTEM");

        // Should not throw: the seeder tops these up on every startup.
        role.Grant(Permissions.Platform.HealthView);
        role.PermissionCodes.ShouldContain(Permissions.Platform.HealthView);
    }

    [Fact]
    public void ACustomRoleCanBeRenamed()
    {
        PlatformRole role = PlatformRole.Create("Old Name", "Old description");

        role.Rename("New Name", "New description");

        role.Name.ShouldBe("New Name");
        role.NormalizedName.ShouldBe("NEW NAME");
    }
}
