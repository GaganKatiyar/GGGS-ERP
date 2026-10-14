using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Domain.Roles;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// The default roles every tenant starts with.
/// <para>
/// These are the only permissions on the platform nobody chose: they arrive with the tenant. That
/// makes them worth asserting on rather than reviewing once - a permission added to a template is
/// a permission silently granted to every customer created afterwards.
/// </para>
/// </summary>
public sealed class RoleTemplateTests
{
    [Fact]
    public void EveryDefinition_CanActuallyBeBuiltIntoARole()
    {
        // Role.Grant refuses codes the catalog does not define and codes reserved for platform
        // staff. Building each template through it is therefore the whole validity check, and it is
        // the same path seeding takes.
        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            Role role = Role.Create(template.Name, template.Description, isSystem: true, templateCode: template.Code);

            foreach (PermissionAssignment permission in template.Permissions)
            {
                role.Grant(permission.PermissionCode, permission.Scope);
            }

            role.Permissions.Count.ShouldBe(
                template.Permissions.Count,
                $"{template.Code} lists the same permission twice.");
        }
    }

    [Fact]
    public void EveryDefinition_HasPermissions()
    {
        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            template.Permissions.ShouldNotBeEmpty($"{template.Code} would be seeded granting nothing.");
        }
    }

    [Fact]
    public void TheCatalogAndTheDefinitions_DescribeTheSameRoles()
    {
        // Drift between the two lists is the failure the seeding service degrades gracefully around
        // by logging a warning. This is where it should be caught instead.
        RoleTemplates.All.Keys.ShouldBe(
            RoleTemplates.Definitions.Select(template => template.Code),
            ignoreOrder: true);
    }

    [Fact]
    public void ExactlyOneTemplate_IsTheFirstAdministratorAndOneIsTheEmployeeDefault()
    {
        RoleTemplates.Definitions.Count(template => template.IsAssignedToFirstAdministrator).ShouldBe(1);
        RoleTemplates.Definitions.Count(template => template.IsDefaultForNewEmployees).ShouldBe(1);

        RoleTemplates.Definitions
            .Single(template => template.IsAssignedToFirstAdministrator).Code
            .ShouldBe(RoleTemplates.TenantAdministratorTemplateCode);

        RoleTemplates.Definitions
            .Single(template => template.IsDefaultForNewEmployees).Code
            .ShouldBe(RoleTemplates.EmployeeTemplateCode);
    }

    [Fact]
    public void OnlyTheAdministratorTemplate_CanHandOutRoles()
    {
        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            bool grantsRoleManagement = template.Permissions
                .Any(permission => permission.PermissionCode == Permissions.Admin.RoleManage);

            grantsRoleManagement.ShouldBe(
                template.Code == RoleTemplates.TenantAdministratorTemplateCode,
                $"{template.Code} should not be able to change what any role may do.");
        }
    }

    /// <summary>
    /// The deliberate withholding, asserted so it cannot drift back.
    /// <para>
    /// Bank details, identity documents and bulk export reach a person's money, their government
    /// identifiers and every record at once. A customer gets them by granting them, which takes one
    /// edit; nobody gets them because of a default we shipped.
    /// </para>
    /// </summary>
    [Fact]
    public void NoTemplate_GrantsSensitiveAccessOrBulkExport()
    {
        string[] withheld =
        [
            Permissions.Employee.BankView,
            Permissions.Employee.BankManage,
            Permissions.Employee.IdentityDocumentView,
            Permissions.Employee.IdentityDocumentManage,
            Permissions.Employee.Export,
            Permissions.Audit.LogExport,
            Permissions.Report.Export,
        ];

        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            foreach (string code in withheld)
            {
                template.Permissions.ShouldNotContain(
                    permission => permission.PermissionCode == code,
                    $"{template.Code} grants {code} by default.");
            }
        }
    }

    [Fact]
    public void TheEmployeeTemplate_ReachesNobodyElsesRecords()
    {
        IReadOnlyList<PermissionAssignment> employee = RoleTemplates.For(RoleTemplates.EmployeeTemplateCode);

        // Two exceptions, neither personal data: the org structure (people need to find a
        // department or a colleague's manager) and the lookup lists every form's dropdowns read.
        employee
            .Where(permission => permission.Scope != DataScope.Own)
            .Select(permission => permission.PermissionCode)
            .ShouldBe([Permissions.Organization.View, Permissions.MasterData.View], ignoreOrder: true);
    }

    [Fact]
    public void TheAuditorTemplate_ChangesNothingBeyondItsOwnAccount()
    {
        string[] writeVerbs = ["manage", "create", "edit", "delete", "adjust", "apply", "cancel", "act", "punch"];

        IEnumerable<string> writes = RoleTemplates.For("AUDITOR")
            .Select(permission => permission.PermissionCode)

            // Self-service is excluded: an auditor is still a person who has to edit their own
            // profile and apply for their own leave.
            .Where(code => !code.StartsWith("self.", StringComparison.Ordinal))
            .Where(code => writeVerbs.Any(verb => code.EndsWith('.' + verb, StringComparison.Ordinal)));

        writes.ShouldBeEmpty();
    }

    [Fact]
    public void EveryTemplate_CarriesSelfService()
    {
        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            template.Permissions
                .Select(permission => permission.PermissionCode)
                .ShouldContain(
                    Permissions.Self.LeaveApply,
                    $"{template.Code} could not apply for their own leave.");
        }
    }

    [Fact]
    public void AnUnknownTemplateCode_ResolvesToNothingRatherThanThrowing()
    {
        // Seeding keeps going on drift, creating a role that grants nothing, because failing the
        // whole event would leave the tenant with no roles at all.
        RoleTemplates.For("NOT_A_TEMPLATE").ShouldBeEmpty();
    }
}
