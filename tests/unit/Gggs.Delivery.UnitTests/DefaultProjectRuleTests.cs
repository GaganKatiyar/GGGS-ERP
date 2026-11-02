using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Project.Domain;
using Shouldly;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Delivery.UnitTests;

public sealed class DefaultProjectRuleTests
{
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();
    private static readonly Guid ParentProject = Guid.NewGuid();
    private static readonly Guid ChildProject = Guid.NewGuid();
    private static readonly Guid OrganizationProject = Guid.NewGuid();

    private static readonly Dictionary<Guid, Guid?> Tree = new() { [Parent] = null, [Child] = Parent };

    [Fact]
    public void ADepartmentWithItsOwnDefault_UsesIt()
        => DefaultProjects.Resolve(Child, Tree, new Dictionary<Guid, Guid> { [Child] = ChildProject, [Parent] = ParentProject }, OrganizationProject).ShouldBe(ChildProject);

    [Fact]
    public void ADepartmentWithoutOne_FallsBackToItsNearestParent()
        => DefaultProjects.Resolve(Child, Tree, new Dictionary<Guid, Guid> { [Parent] = ParentProject }, OrganizationProject).ShouldBe(ParentProject);

    [Fact]
    public void WithNothingUpTheTree_TheOrganisationsDefaultIsUsed()
        => DefaultProjects.Resolve(Child, Tree, new Dictionary<Guid, Guid>(), OrganizationProject).ShouldBe(OrganizationProject);

    [Fact]
    public void WithoutADepartment_TheOrganisationsDefaultIsUsed()
        => DefaultProjects.Resolve(null, Tree, new Dictionary<Guid, Guid> { [Parent] = ParentProject }, OrganizationProject).ShouldBe(OrganizationProject);

    [Fact]
    public void ACycleInTheTree_DoesNotHang()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var cycle = new Dictionary<Guid, Guid?> { [first] = second, [second] = first };

        DefaultProjects.Resolve(first, cycle, new Dictionary<Guid, Guid>(), OrganizationProject).ShouldBe(OrganizationProject);
    }

    [Theory]
    [InlineData("ENG", "D_ENG")]
    [InlineData("eng-ops", "D_ENGOPS")]
    [InlineData("---", "D_DEPT")]
    [InlineData("A_VERY_LONG_DEPARTMENT_CODE", "D_A_VERY_LONG_DE")]
    public void ACode_IsCleanedToWhatAProjectCodeAllows(string department, string expected)
    {
        string code = DefaultProjects.CodeFor(department, new HashSet<string>());

        code.ShouldBe(expected);
        ProjectEntity.NormalizeCode(code).ShouldBe(expected);
    }

    [Fact]
    public void ACodeAlreadyTaken_GetsASuffixWithinTheLengthLimit()
    {
        DefaultProjects.CodeFor("ENG", new HashSet<string> { "D_ENG" }).ShouldBe("D_ENG2");
        DefaultProjects.CodeFor("A_VERY_LONG_DEPARTMENT_CODE", new HashSet<string> { "D_A_VERY_LONG_DE" }).ShouldBe("D_A_VERY_LONG_D2");
    }

    [Fact]
    public void EveryDefaultActivity_HasSomethingToBookTo()
    {
        DefaultProjects.Activities.ShouldNotBeEmpty();
        DefaultProjects.Activities.ShouldAllBe(activity => activity.Tasks.Count > 0);
    }

    [Theory]
    [InlineData(true, ProjectBillingType.NonBillable)]
    [InlineData(false, ProjectBillingType.TimeAndMaterial)]
    public void ADefaultProject_HasNoClientAndIsNotBilled(bool withClient, ProjectBillingType billing)
    {
        DomainRuleException error = Should.Throw<DomainRuleException>(() => ProjectEntity.Create("D_ENG", Profile(withClient ? Guid.NewGuid() : null, billing), ProjectStage.Execution));

        error.ErrorCode.ShouldBe("PROJECT_DEFAULT_TERMS");
    }

    [Fact]
    public void ADefaultProject_WithNoClientAndNoBilling_IsAccepted()
        => ProjectEntity.Create("D_ENG", Profile(null, ProjectBillingType.NonBillable), ProjectStage.Execution).Type.ShouldBe(ProjectType.DepartmentDefault);

    private static ProjectProfile Profile(Guid? clientId, ProjectBillingType billing) => new(
        "Engineering (default)", ProjectType.DepartmentDefault, null, clientId, null, null, Guid.NewGuid(), null, null, null, new DateOnly(2026, 1, 1), DefaultProjects.OpenEnded,
        null, WorkPriority.Medium, billing, "INR", null, null, null, null, null, null, Confidentiality.Internal, DeliveryMethodology.Agile);
}
