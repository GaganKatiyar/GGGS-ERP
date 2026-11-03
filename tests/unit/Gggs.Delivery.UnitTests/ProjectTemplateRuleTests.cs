using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Project.Domain;
using Shouldly;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Delivery.UnitTests;

public sealed class ProjectTemplateRuleTests
{
    private static readonly TemplateColumn New = new("New", ColumnCategory.Backlog);
    private static readonly TemplateColumn Done = new("Done", ColumnCategory.Done);

    private static TemplateDefinition Board(params TemplateColumn[] columns) => new(["Build"], columns, []);

    [Fact]
    public void EveryEngagementModelWithRevenue_HasADefaultTemplate()
    {
        ProjectBillingType[] covered = [.. ProjectTemplateCatalog.Defaults().Select(template => template.BillingType)];

        foreach (ProjectBillingType model in Enum.GetValues<ProjectBillingType>().Where(model => model != ProjectBillingType.NonBillable))
        {
            covered.ShouldContain(model);
        }
    }

    [Fact]
    public void ADefinition_SurvivesBeingStored()
    {
        ProjectTemplate template = ProjectTemplate.Create("T", "Template", ProjectBillingType.Retainer, new TemplateDefinition([" Build "], [New, Done], [new DefaultActivity("Meetings", ["Stand-up", " "])]));

        TemplateDefinition stored = template.Definition;

        stored.Phases.ShouldBe(["Build"]);
        stored.Columns.Select(column => column.Category).ShouldBe([ColumnCategory.Backlog, ColumnCategory.Done]);
        stored.Activities.Single().Tasks.ShouldBe(["Stand-up"], "blank lines are dropped");
    }

    [Theory]
    [InlineData(ColumnCategory.Done)]
    [InlineData(ColumnCategory.Backlog)]
    public void ABoard_NeedsSomewhereForNewWorkAndForFinishedWork(ColumnCategory only)
        => Should.Throw<DomainRuleException>(() => ProjectTemplate.Create("T", "T", ProjectBillingType.Retainer, Board(new TemplateColumn("Only", only)))).ErrorCode.ShouldBe("PROJECT_TEMPLATE_COLUMNS");

    [Fact]
    public void TwoColumns_CannotShareAName()
        => Should.Throw<DomainRuleException>(() => ProjectTemplate.Create("T", "T", ProjectBillingType.Retainer, Board(New, Done, new TemplateColumn("done", ColumnCategory.InProgress)))).ErrorCode.ShouldBe("PROJECT_TEMPLATE_COLUMNS");

    [Fact]
    public void AMilestoneNeedingAcceptance_IsAchievedByBeingAccepted()
    {
        ProjectMilestone milestone = ProjectMilestone.Create(Guid.NewGuid(), new MilestonePlan("Beta", null, new DateOnly(2026, 11, 1), null, null, MilestoneStatus.InProgress, 40, true, RequiresAcceptance: true));

        milestone.Accept(new DateOnly(2026, 11, 3), " Asha Rao ", "Evaluation: precision 0.94");

        milestone.Status.ShouldBe(MilestoneStatus.Achieved);
        milestone.ActualDate.ShouldBe(new DateOnly(2026, 11, 3));
        milestone.AcceptedBy.ShouldBe("Asha Rao");
        milestone.AcceptanceEvidence.ShouldBe("Evaluation: precision 0.94");
    }

    [Fact]
    public void Reopening_AnAcceptedMilestone_ClearsTheAcceptance()
    {
        ProjectMilestone milestone = ProjectMilestone.Create(Guid.NewGuid(), new MilestonePlan("Beta", null, new DateOnly(2026, 11, 1), null, null, MilestoneStatus.Planned, 0, true, RequiresAcceptance: true));
        milestone.Accept(new DateOnly(2026, 11, 3), "Asha Rao", null);

        milestone.Update(new MilestonePlan("Beta", null, new DateOnly(2026, 11, 1), null, null, MilestoneStatus.InProgress, 50, true, RequiresAcceptance: true));

        milestone.AcceptedOn.ShouldBeNull();
        milestone.AcceptedBy.ShouldBeNull();
    }

    [Fact]
    public void ABaseline_FixesTheCommittedEndAndTheEffort()
    {
        ProjectEntity project = ProjectEntity.Create("P", new ProjectProfile(
            "Project", ProjectType.Implementation, null, null, null, null, null, null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 15),
            WorkPriority.Medium, ProjectBillingType.FixedPrice, "INR", null, null, null, null, null, null, Confidentiality.Internal, DeliveryMethodology.Agile), ProjectStage.Planning);

        project.CaptureBaseline(800m, new DateOnly(2026, 1, 5));

        project.BaselineEndDate.ShouldBe(new DateOnly(2026, 6, 30), "the commitment, not the forecast");
        project.BaselineHours.ShouldBe(800m);
        project.BaselinedOn.ShouldBe(new DateOnly(2026, 1, 5));
    }
}
