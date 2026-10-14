using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Workflow.Domain;
using Shouldly;

namespace Gggs.Workflow.UnitTests;

public sealed class StageConditionTests
{
    private static readonly Dictionary<string, string> Request = new() { ["amount"] = "50000", ["category"] = "Travel" };

    [Theory]
    [InlineData(ConditionOperator.GreaterThan, "10000", true)]
    [InlineData(ConditionOperator.GreaterThan, "50000", false)]
    [InlineData(ConditionOperator.GreaterThanOrEqual, "50000", true)]
    [InlineData(ConditionOperator.LessThan, "50000.01", true)]
    [InlineData(ConditionOperator.LessThanOrEqual, "49999", false)]
    [InlineData(ConditionOperator.Equals, "50000.00", true)]
    [InlineData(ConditionOperator.NotEquals, "50000", false)]
    public void NumbersCompareAsNumbers(ConditionOperator op, string value, bool expected)
        => new StageCondition("amount", op, value).IsMet(Request).ShouldBe(expected);

    [Fact]
    public void TextComparesWithoutCase_AndInMatchesAnyListedValue()
    {
        new StageCondition("category", ConditionOperator.Equals, "travel").IsMet(Request).ShouldBeTrue();
        new StageCondition("category", ConditionOperator.In, "Training, TRAVEL").IsMet(Request).ShouldBeTrue();
        new StageCondition("category", ConditionOperator.In, "Training,Equipment").IsMet(Request).ShouldBeFalse();
    }

    [Fact]
    public void AFieldTheRequestDoesNotCarry_MeansTheStageDoesNotApply()
        => new StageCondition("region", ConditionOperator.Equals, "EU").IsMet(Request).ShouldBeFalse();

    [Fact]
    public void ANumericOperator_OnText_IsNotMet()
        => new StageCondition("category", ConditionOperator.GreaterThan, "10").IsMet(Request).ShouldBeFalse();
}

public sealed class StageValidationTests
{
    private static WorkflowStage Valid(string name = "Manager") => new(name, StageMode.All, [new ApproverRule(ApproverKind.ReportingManager)]);

    [Fact]
    public void AWorkflowNeedsBetweenOneAndTenStages()
    {
        Should.Throw<DomainRuleException>(() => StageJson.Validate([])).ErrorCode.ShouldBe("WORKFLOW_STAGE_COUNT");
        Should.Throw<DomainRuleException>(() => StageJson.Validate([.. Enumerable.Range(0, 11).Select(index => Valid($"S{index}"))]))
            .ErrorCode.ShouldBe("WORKFLOW_STAGE_COUNT");
        Should.NotThrow(() => StageJson.Validate([Valid()]));
    }

    [Fact]
    public void AnEmployeeApprover_MustNameTheEmployee()
        => Should.Throw<DomainRuleException>(() => StageJson.Validate([new WorkflowStage("Finance", StageMode.All, [new ApproverRule(ApproverKind.Employee)])]))
            .ErrorCode.ShouldBe("WORKFLOW_APPROVER_EMPLOYEE");

    [Fact]
    public void AnSla_IsBetweenAnHourAndAMonth()
        => Should.Throw<DomainRuleException>(() => StageJson.Validate([Valid() with { SlaHours = 0 }])).ErrorCode.ShouldBe("WORKFLOW_STAGE_SLA");

    [Fact]
    public void StagesRoundTripThroughJson()
    {
        WorkflowStage stage = new(
            "Finance",
            StageMode.Any,
            [new ApproverRule(ApproverKind.Employee, Guid.NewGuid()), new ApproverRule(ApproverKind.DepartmentHead)],
            new StageCondition("amount", ConditionOperator.GreaterThan, "1000"),
            SlaHours: 48,
            EscalateTo: new ApproverRule(ApproverKind.SkipLevelManager));

        WorkflowStage read = StageJson.Read(StageJson.Write([stage]))[0];

        read.Name.ShouldBe("Finance");
        read.Mode.ShouldBe(StageMode.Any);
        read.Approvers.ShouldBe(stage.Approvers);
        read.Condition.ShouldBe(stage.Condition);
        read.EscalateTo.ShouldBe(stage.EscalateTo);
        StageJson.Write([stage]).ShouldContain("\"DepartmentHead\"", Case.Sensitive, "enums are stored by name, so reordering them never changes meaning");
    }
}

public sealed class DelegationTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public void ADelegationCoversItsDays_AndOptionallyOneKindOfRequest()
    {
        Delegation all = Delegation.Create(Guid.NewGuid(), Guid.NewGuid(), Monday, Monday.AddDays(4), null, "Leave");
        all.Covers("leave.request", Monday.AddDays(2)).ShouldBeTrue();
        all.Covers("leave.request", Monday.AddDays(5)).ShouldBeFalse();

        Delegation onlyPolicies = Delegation.Create(Guid.NewGuid(), Guid.NewGuid(), Monday, Monday, "policy.publish", null);
        onlyPolicies.Covers("policy.publish", Monday).ShouldBeTrue();
        onlyPolicies.Covers("leave.request", Monday).ShouldBeFalse();

        onlyPolicies.Revoke();
        onlyPolicies.Covers("policy.publish", Monday).ShouldBeFalse();
    }

    [Fact]
    public void ADelegationCannotGoToOneself_RunBackwards_OrLastOverAYear()
    {
        Guid me = Guid.NewGuid();
        Should.Throw<DomainRuleException>(() => Delegation.Create(me, me, Monday, Monday, null, null)).ErrorCode.ShouldBe("DELEGATION_SELF");
        Should.Throw<DomainRuleException>(() => Delegation.Create(me, Guid.NewGuid(), Monday, Monday.AddDays(-1), null, null)).ErrorCode.ShouldBe("DELEGATION_DATES");
        Should.Throw<DomainRuleException>(() => Delegation.Create(me, Guid.NewGuid(), Monday, Monday.AddDays(400), null, null)).ErrorCode.ShouldBe("DELEGATION_TOO_LONG");
    }
}

public sealed class InstanceLifecycleTests
{
    private static WorkflowInstance Pending()
        => WorkflowInstance.Start(null, "test.approval", Guid.NewGuid(), "Request", null, Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, string>(), DateTime.UtcNow);

    [Fact]
    public void ASentBackRequest_ResubmitsAsTheNextRound()
    {
        WorkflowInstance instance = Pending();
        instance.SendBack();
        instance.IsOpen.ShouldBeTrue("a sent-back request still belongs to its requester");

        instance.Resubmit(DateTime.UtcNow);
        instance.Status.ShouldBe(WorkflowInstanceStatus.Pending);
        instance.Round.ShouldBe(2);
    }

    [Fact]
    public void ADecidedRequest_TakesNoFurtherDecisions()
    {
        WorkflowInstance instance = Pending();
        instance.Approve(DateTime.UtcNow);

        Should.Throw<DomainRuleException>(() => instance.Reject(DateTime.UtcNow)).ErrorCode.ShouldBe("WORKFLOW_CLOSED");
        Should.Throw<DomainRuleException>(() => instance.Resubmit(DateTime.UtcNow)).ErrorCode.ShouldBe("WORKFLOW_STATE");
    }

    [Fact]
    public void ATaskIsDecidedOnce()
    {
        WorkflowInstance instance = Pending();
        WorkflowTask task = WorkflowTask.Assign(instance, 0, "Manager", Guid.NewGuid(), null, null);

        task.Decide(WorkflowTaskStatus.Approved, Guid.NewGuid(), null, DateTime.UtcNow);
        Should.Throw<DomainRuleException>(() => task.Decide(WorkflowTaskStatus.Rejected, Guid.NewGuid(), "no", DateTime.UtcNow))
            .ErrorCode.ShouldBe("WORKFLOW_TASK_CLOSED");
        Should.Throw<ArgumentOutOfRangeException>(() => WorkflowTask.Assign(instance, 0, "Manager", Guid.NewGuid(), null, null)
            .Decide(WorkflowTaskStatus.Closed, Guid.NewGuid(), null, DateTime.UtcNow));
    }
}
