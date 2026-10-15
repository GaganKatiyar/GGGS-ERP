using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Contract.Domain;
using Gggs.Modules.Project.Domain;
using Shouldly;
using ContractEntity = Gggs.Modules.Contract.Domain.Contract;

namespace Gggs.Delivery.UnitTests;

public sealed class HealthCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static HealthInputs Healthy() => new(
        Today, Today.AddDays(-50), Today.AddDays(50), ProgressPercent: 50, MeasuredTaskCount: 10, MilestoneDaysOverdue: [], MilestoneCount: 2,
        ActiveTeamCount: 4, HasProjectManager: true, MembersEndingSoon: 0, PendingChangeRequests: 0, ApprovedScheduleImpactDays: 0,
        OpenBugs: 1, OpenHighBugs: 0, OpenCriticalBugs: 0, MaxOpenRiskScore: 4, OpenRisks: 1, OpenCriticalIssues: 0, OpenHighIssues: 0,
        OverdueIssues: 0, ClientSatisfaction: 5);

    [Fact]
    public void AHealthyProject_IsGreen_WithBudgetLeftUnassessed()
    {
        HealthResult result = HealthCalculator.Assess(Healthy(), ProjectHealthThresholds.Default);

        result.Overall.ShouldBe(HealthRating.Green);
        result.Budget.ShouldBe(HealthRating.NotAssessed);
        result.Schedule.ShouldBe(HealthRating.Green);
    }

    [Fact]
    public void OneRedAmongHealthyDimensions_ReadsAmberOverall_AsTheSpecExampleDoes()
    {
        // Schedule, quality green; resources amber; risk red -> overall amber (spec section 78).
        HealthResult result = HealthCalculator.Assess(Healthy() with { HasProjectManager = false, MaxOpenRiskScore = 20 }, ProjectHealthThresholds.Default);

        result.Resources.ShouldBe(HealthRating.Amber);
        result.Risk.ShouldBe(HealthRating.Red);
        result.Overall.ShouldBe(HealthRating.Amber);
    }

    [Fact]
    public void TwoReds_ReadRed()
    {
        HealthResult result = HealthCalculator.Assess(Healthy() with { MaxOpenRiskScore = 20, OpenCriticalBugs = 1 }, ProjectHealthThresholds.Default);
        result.Overall.ShouldBe(HealthRating.Red);
    }

    [Fact]
    public void ProgressTrailingTime_TurnsScheduleAmberThenRed()
    {
        // Half the time gone: 38% done is 12 points behind (amber), 20% is 30 behind (red).
        HealthCalculator.Assess(Healthy() with { ProgressPercent = 38 }, ProjectHealthThresholds.Default).Schedule.ShouldBe(HealthRating.Amber);
        HealthCalculator.Assess(Healthy() with { ProgressPercent = 20 }, ProjectHealthThresholds.Default).Schedule.ShouldBe(HealthRating.Red);
    }

    [Fact]
    public void ALateMilestone_TurnsScheduleAmber_AndAVeryLateOneRed()
    {
        HealthCalculator.Assess(Healthy() with { MilestoneDaysOverdue = [3] }, ProjectHealthThresholds.Default).Schedule.ShouldBe(HealthRating.Amber);
        HealthCalculator.Assess(Healthy() with { MilestoneDaysOverdue = [20] }, ProjectHealthThresholds.Default).Schedule.ShouldBe(HealthRating.Red);
    }

    [Fact]
    public void NothingToMeasure_LeavesScheduleUnassessed_AndAnEmptyTeamIsRed()
    {
        HealthResult result = HealthCalculator.Assess(Healthy() with { MeasuredTaskCount = 0, MilestoneCount = 0, ActiveTeamCount = 0, ClientSatisfaction = null }, ProjectHealthThresholds.Default);
        result.Schedule.ShouldBe(HealthRating.NotAssessed);
        result.ClientSatisfaction.ShouldBe(HealthRating.NotAssessed);
        result.Resources.ShouldBe(HealthRating.Red);
    }

    [Fact]
    public void Thresholds_AreTheTenants()
    {
        ProjectHealthThresholds lenient = ProjectHealthThresholds.Default with { RiskAmberScore = 21, RiskRedScore = 25 };
        HealthCalculator.Assess(Healthy() with { MaxOpenRiskScore = 20 }, lenient).Risk.ShouldBe(HealthRating.Green);
    }

    [Fact]
    public void Settings_RejectRedBelowAmber()
    {
        ProjectHealthSettings settings = ProjectHealthSettings.Defaults();
        Should.Throw<DomainRuleException>(() => settings.Update(ProjectHealthThresholds.Default with { RiskAmberScore = 15, RiskRedScore = 10 }));
    }
}

public sealed class PlanningRuleTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void ADependency_ThatClosesALoop_IsDetected()
    {
        (Guid, Guid)[] edges = [(A, B), (B, C)];

        DependencyGraph.WouldCreateCycle(edges, C, A).ShouldBeTrue("A already leads to C");
        DependencyGraph.WouldCreateCycle(edges, A, C).ShouldBeFalse("a shortcut is not a loop");
        DependencyGraph.WouldCreateCycle(edges, A, A).ShouldBeTrue();
    }

    [Theory]
    [InlineData(TaskType.Story, TaskType.Epic, true)]
    [InlineData(TaskType.Task, TaskType.Story, true)]
    [InlineData(TaskType.Subtask, TaskType.Task, true)]
    [InlineData(TaskType.Subtask, TaskType.Epic, false)]
    [InlineData(TaskType.Epic, TaskType.Epic, false)]
    [InlineData(TaskType.Story, TaskType.Task, false)]
    public void TheHierarchy_AllowsOnlyDownwardNesting(TaskType child, TaskType parent, bool allowed)
        => TaskHierarchy.CanHaveParent(child, parent).ShouldBe(allowed);

    [Fact]
    public void ASubtask_NeedsAParent()
        => TaskHierarchy.CanHaveParent(TaskType.Subtask, null).ShouldBeFalse();

    [Fact]
    public void TheBurndown_CountsPointsLeftEachDay_AndStopsAtToday()
    {
        DateOnly start = new(2026, 10, 1);
        IReadOnlyList<BurndownPoint> points = Burndown.Compute(start, start.AddDays(4), start.AddDays(2), [(5, start.AddDays(1)), (3, null)]);

        points.Count.ShouldBe(5);
        points[0].Remaining.ShouldBe(8);
        points[1].Remaining.ShouldBe(3);
        points[2].Remaining.ShouldBe(3);
        points[3].Remaining.ShouldBeNull("the future has not happened yet");
        points[0].Ideal.ShouldBe(8);
        points[4].Ideal.ShouldBe(0);
    }

    [Fact]
    public void Progress_WeighsByPoints_ThenHours_ThenCount_AndIgnoresEpics()
    {
        ProgressMeter.Of([(TaskType.Story, 8, null, 0, true), (TaskType.Story, 2, null, 0, false), (TaskType.Epic, 100, null, 0, false)])
            .Percent.ShouldBe(80m);
        ProgressMeter.Of([(TaskType.Task, null, 10m, 50, false), (TaskType.Task, null, 30m, 0, false)]).Percent.ShouldBe(12.5m);
        ProgressMeter.Of([(TaskType.Task, null, null, 0, true), (TaskType.Task, null, null, 0, false)]).Percent.ShouldBe(50m);
        ProgressMeter.Of([]).Measured.ShouldBe(0);
    }
}

public sealed class GovernanceRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static RaidDetails Risk(int probability, int impact) => new(
        "Vendor slips", null, null, probability, impact, null, WorkPriority.High, null, null, null, null, null, null, null, RaidStatus.Open);

    [Fact]
    public void ARisk_IsScoredProbabilityTimesImpact()
    {
        RaidItem risk = RaidItem.Create(Guid.NewGuid(), 1, RaidKind.Risk, Risk(3, 4), Today);
        risk.Score.ShouldBe(12);
        risk.Code.ShouldBe("RSK-1");
        Should.Throw<DomainRuleException>(() => RaidItem.Create(Guid.NewGuid(), 2, RaidKind.Risk, Risk(0, 4), Today));
    }

    [Fact]
    public void OnlyARisk_CanBeAccepted()
    {
        RaidDetails accepted = Risk(1, 1) with { Status = RaidStatus.Accepted, Severity = Severity.Low };
        Should.Throw<DomainRuleException>(() => RaidItem.Create(Guid.NewGuid(), 1, RaidKind.Issue, accepted, Today));
        RaidItem.Create(Guid.NewGuid(), 1, RaidKind.Risk, accepted, Today).Status.ShouldBe(RaidStatus.Accepted);
    }

    [Fact]
    public void AChangeRequest_MovesOneStepAtATime_AndFreezesOnceSubmitted()
    {
        var details = new ChangeRequestDetails("Add UPI", "Payments", null, null, 10, 1000, 5, WorkPriority.Medium, null, "Client");
        ChangeRequest request = ChangeRequest.Draft(Guid.NewGuid(), 1, details);
        request.SubmitForApproval(Guid.NewGuid());

        Should.Throw<DomainRuleException>(() => request.Edit(details));
        request.Approve(DateTime.UtcNow);
        Should.Throw<DomainRuleException>(() => request.Advance(ChangeRequestStatus.Testing, Today));

        request.Advance(ChangeRequestStatus.Implementing, Today);
        request.ImplementationDate.ShouldBe(Today);
        request.Advance(ChangeRequestStatus.Testing, Today);
        request.Advance(ChangeRequestStatus.Closed, Today);
        request.ClosedOn.ShouldBe(Today);
        Should.Throw<DomainRuleException>(() => request.Withdraw());
    }
}

public sealed class ContractRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static ContractTerms Terms(DateOnly start, DateOnly? end, bool autoRenew = false, DateOnly? renewal = null) => new(
        "MSA", ContractType.Msa, null, start, end, "INR", 100, BillingModel.TimeAndMaterial, 30, null, null, renewal, autoRenew, null, null, null);

    [Fact]
    public void AnEndedContract_Expires_UnlessItRenewsItself()
    {
        var ending = ContractEntity.Draft("C-1", Guid.NewGuid(), Terms(Today.AddDays(-30), Today.AddDays(-1)));
        ending.Activate(DateTime.UtcNow, Today.AddDays(-2));
        ending.ExpireIfDue(Today).ShouldBeTrue();
        ending.Status.ShouldBe(ContractStatus.Expired);

        // Thirty days long, ended yesterday: it rolls on by another thirty.
        var rolling = ContractEntity.Draft("C-2", Guid.NewGuid(), Terms(Today.AddDays(-30), Today.AddDays(-1), autoRenew: true));
        rolling.Activate(DateTime.UtcNow, Today.AddDays(-2));
        rolling.ExpireIfDue(Today).ShouldBeTrue();
        rolling.Status.ShouldBe(ContractStatus.Active);
        rolling.EndDate.ShouldBe(Today.AddDays(29));
    }

    [Fact]
    public void ARenewalReminder_GoesOutOncePerDate_WithinTheLeadTime()
    {
        var contract = ContractEntity.Draft("C-3", Guid.NewGuid(), Terms(Today.AddDays(-100), Today.AddDays(60), renewal: Today.AddDays(20)));
        contract.Activate(DateTime.UtcNow, Today);

        contract.NeedsRenewalReminder(Today, 30).ShouldBeTrue();
        contract.NeedsRenewalReminder(Today, 10).ShouldBeFalse("outside the lead time");
        contract.RecordRenewalReminder();
        contract.NeedsRenewalReminder(Today, 30).ShouldBeFalse();
    }

    [Fact]
    public void ASignedContract_KeepsItsTypeAndCurrency_ButCanBeExtended()
    {
        var contract = ContractEntity.Draft("C-4", Guid.NewGuid(), Terms(Today.AddDays(-100), Today.AddDays(-5)));
        contract.Activate(DateTime.UtcNow, Today);
        contract.Status.ShouldBe(ContractStatus.Expired);

        Should.Throw<DomainRuleException>(() => contract.Amend(Terms(Today.AddDays(-100), Today.AddDays(30)) with { Currency = "USD" }, Today));
        contract.Amend(Terms(Today.AddDays(-100), Today.AddDays(30)), Today);
        contract.Status.ShouldBe(ContractStatus.Active, "moving the end date into the future renews it");
    }

    [Fact]
    public void AnMsa_DoesNotSitUnderAnotherContract()
        => Should.Throw<DomainRuleException>(() => ContractEntity.Draft("C-5", Guid.NewGuid(), Terms(Today, null) with { ParentContractId = Guid.NewGuid() }));
}
