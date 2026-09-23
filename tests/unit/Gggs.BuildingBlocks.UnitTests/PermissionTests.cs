using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Catalog_ContainsEveryDeclaredPermissionConstant()
    {
        PermissionCatalog.All.ShouldNotBeEmpty();
        PermissionCatalog.Exists(Permissions.Leave.RequestCreate).ShouldBeTrue();
        PermissionCatalog.Exists(Permissions.Platform.TenantManage).ShouldBeTrue();
    }

    [Fact]
    public void EveryPermissionCode_FollowsModuleResourceActionFormat()
    {
        foreach (PermissionDefinition definition in PermissionCatalog.All)
        {
            definition.Code.Split('.').Length.ShouldBe(
                3,
                $"'{definition.Code}' must be module.resource.action so roles and the audit trail stay readable.");

            definition.Code.ShouldBe(definition.Code.ToLowerInvariant());
        }
    }

    [Fact]
    public void SensitivePermissions_AreFlaggedSoTheyRequireStepUpAndAreAudited()
    {
        PermissionCatalog.IsSensitive(Permissions.Employee.BankView).ShouldBeTrue();
        PermissionCatalog.IsSensitive(Permissions.Employee.IdentityDocumentView).ShouldBeTrue();
        PermissionCatalog.IsSensitive(Permissions.Employee.Export).ShouldBeTrue();

        PermissionCatalog.IsSensitive(Permissions.Organization.View).ShouldBeFalse();
    }

    [Fact]
    public void PlatformPermissions_AreNotOfferedToTenantRoles()
    {
        PermissionCatalog.Find(Permissions.Platform.TenantManage)!.IsPlatformOnly.ShouldBeTrue();
        PermissionCatalog.Find(Permissions.Admin.RoleManage)!.IsPlatformOnly.ShouldBeFalse();
    }

    [Fact]
    public void UnknownCode_IsRejected()
    {
        // Endpoints fail closed on a typo rather than letting everyone through.
        PermissionCatalog.Exists("leave.request.approv").ShouldBeFalse();
    }
}

public sealed class PermissionSetTests
{
    [Fact]
    public void WidestScopeWins_WhenTheSamePermissionComesFromSeveralRoles()
    {
        // A user who is both a team lead (own team) and an HR manager (whole branch) should get
        // the branch-wide view, not whichever role happened to be read first.
        var set = new PermissionSet(
            [
                new PermissionGrant { Code = Permissions.Leave.RequestView, Scope = DataScope.DirectReports },
                new PermissionGrant { Code = Permissions.Leave.RequestView, Scope = DataScope.Branch },
            ],
            permissionVersion: 3);

        set.ScopeFor(Permissions.Leave.RequestView).ShouldBe(DataScope.Branch);
        set.Count.ShouldBe(1);
    }

    [Fact]
    public void Has_IsCaseInsensitive()
    {
        var set = new PermissionSet(
            [new PermissionGrant { Code = "Leave.Request.View", Scope = DataScope.Own }],
            permissionVersion: 1);

        set.Has(Permissions.Leave.RequestView).ShouldBeTrue();
    }

    [Fact]
    public void Empty_GrantsNothing()
    {
        PermissionSet.Empty.Has(Permissions.Self.ProfileView).ShouldBeFalse();
        PermissionSet.Empty.ScopeFor(Permissions.Self.ProfileView).ShouldBeNull();
    }
}
