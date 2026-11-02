using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Roles;
using Gggs.Modules.Identity.Domain.Roles;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>Which automatic role grants the org chart calls for, and which no longer.</summary>
public sealed class RoleAutomationPlanTests
{
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid DepartmentHead = Guid.NewGuid();
    private static readonly AutomaticRole ManagerRole = new(Manager, RoleAutoAssign.PeopleManagers);
    private static readonly AutomaticRole HeadRole = new(DepartmentHead, RoleAutoAssign.DepartmentHeads);

    private static readonly Guid Vikram = Guid.NewGuid();
    private static readonly Guid VikramEmployee = Guid.NewGuid();
    private static readonly Guid Meera = Guid.NewGuid();
    private static readonly Guid MeeraEmployee = Guid.NewGuid();
    private static readonly EmployeeLink[] Links = [new(Vikram, VikramEmployee), new(Meera, MeeraEmployee)];

    private static readonly Guid Engineering = Guid.NewGuid();
    private static readonly Guid Platform = Guid.NewGuid();

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> NoHeads = new Dictionary<Guid, IReadOnlyList<Guid>>();

    [Fact]
    public void AManagerGetsTheRole_TenantWide_AndSomeoneWithoutReportsDoesNot()
    {
        RoleAutomationChanges changes = RoleAutomationPlan.Work([ManagerRole], Links, new HashSet<Guid> { VikramEmployee }, NoHeads, []);

        changes.ToGrant.ShouldBe([new WantedGrant(Vikram, Manager, null)]);
        changes.ToEnd.ShouldBeEmpty();
    }

    [Fact]
    public void ADepartmentHeadGetsOneGrantPerDepartment_AnchoredToIt()
    {
        var heads = new Dictionary<Guid, IReadOnlyList<Guid>> { [VikramEmployee] = [Engineering, Platform] };

        RoleAutomationChanges changes = RoleAutomationPlan.Work([HeadRole], Links, new HashSet<Guid>(), heads, []);

        changes.ToGrant.Select(want => want.DepartmentId).ShouldBe([Engineering, Platform], ignoreOrder: true);
        changes.ToGrant.ShouldAllBe(want => want.UserId == Vikram && want.RoleId == DepartmentHead);
    }

    [Fact]
    public void AGrantAlreadyInPlace_IsLeftAlone()
    {
        ActiveGrant existing = new(Guid.NewGuid(), Vikram, Manager, null, null, IsAutomatic: true);

        RoleAutomationPlan.Work([ManagerRole], Links, new HashSet<Guid> { VikramEmployee }, NoHeads, [existing]).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheReportsMoveAway_TheAutomaticGrantEnds()
    {
        ActiveGrant existing = new(Guid.NewGuid(), Vikram, Manager, null, null, IsAutomatic: true);

        RoleAutomationChanges changes = RoleAutomationPlan.Work([ManagerRole], Links, new HashSet<Guid>(), NoHeads, [existing]);

        changes.ToEnd.ShouldBe([existing.GrantId]);
        changes.ToGrant.ShouldBeEmpty();
    }

    [Fact]
    public void ARoleSwitchedOff_EndsTheGrantsItMadeAutomatically()
    {
        ActiveGrant existing = new(Guid.NewGuid(), Vikram, Manager, null, null, IsAutomatic: true);

        RoleAutomationPlan.Work([], Links, new HashSet<Guid> { VikramEmployee }, NoHeads, [existing]).ToEnd.ShouldBe([existing.GrantId]);
    }

    [Fact]
    public void AGrantByHand_IsNeverEnded_AndNeedsNoAutomaticCopy()
    {
        // Meera was made a Manager by hand without any reports; Vikram holds it by hand and has reports.
        ActiveGrant meeraByHand = new(Guid.NewGuid(), Meera, Manager, null, null, IsAutomatic: false);
        ActiveGrant vikramByHand = new(Guid.NewGuid(), Vikram, Manager, null, null, IsAutomatic: false);

        RoleAutomationChanges changes = RoleAutomationPlan.Work([ManagerRole], Links, new HashSet<Guid> { VikramEmployee }, NoHeads, [meeraByHand, vikramByHand]);

        changes.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void AnAnchoredGrantByHand_CoversItsOwnDepartmentOnly()
    {
        var heads = new Dictionary<Guid, IReadOnlyList<Guid>> { [VikramEmployee] = [Engineering, Platform] };
        ActiveGrant engineeringByHand = new(Guid.NewGuid(), Vikram, DepartmentHead, RoleScopeType.Department, Engineering, IsAutomatic: false);

        RoleAutomationChanges changes = RoleAutomationPlan.Work([HeadRole], Links, new HashSet<Guid>(), heads, [engineeringByHand]);

        changes.ToGrant.ShouldBe([new WantedGrant(Vikram, DepartmentHead, Platform)]);
    }

    [Fact]
    public void TheBuiltInManagerAndDepartmentHeadRoles_FollowTheOrgChart_AndNoOtherDoes()
    {
        RoleTemplates.Definitions.Single(template => template.Code == "TEAM_LEAD").AutoAssign.ShouldBe(RoleAutoAssign.PeopleManagers);
        RoleTemplates.Definitions.Single(template => template.Code == "DEPARTMENT_MANAGER").AutoAssign.ShouldBe(RoleAutoAssign.DepartmentHeads);
        RoleTemplates.Definitions.Count(template => template.AutoAssign != RoleAutoAssign.None).ShouldBe(2);
    }
}
