using System.Text.Json;
using Gggs.Workflow.Application.Workflows;
using Shouldly;

namespace Gggs.Workflow.UnitTests;

public sealed class WorkflowConditionEvaluatorTests
{
    [Fact]
    public void Evaluate_WithNoCondition_AlwaysEntersTheStep()
    {
        WorkflowConditionEvaluator.Evaluate(condition: null, Context("{}")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"==": [{"var": "leaveTypeCode"}, "CASUAL"]}""", """{"leaveTypeCode": "CASUAL"}""", true)]
    [InlineData("""{"==": [{"var": "leaveTypeCode"}, "CASUAL"]}""", """{"leaveTypeCode": "SICK"}""", false)]
    [InlineData("""{"!=": [{"var": "leaveTypeCode"}, "CASUAL"]}""", """{"leaveTypeCode": "SICK"}""", true)]
    public void Evaluate_Equality(string conditionJson, string contextJson, bool expected)
        => WorkflowConditionEvaluator.Evaluate(Condition(conditionJson), Context(contextJson)).ShouldBe(expected);

    [Theory]
    [InlineData("""{">": [{"var": "days"}, 3]}""", """{"days": 5}""", true)]
    [InlineData("""{">": [{"var": "days"}, 3]}""", """{"days": 3}""", false)]
    [InlineData("""{">=": [{"var": "days"}, 3]}""", """{"days": 3}""", true)]
    [InlineData("""{"<": [{"var": "days"}, 3]}""", """{"days": 2}""", true)]
    [InlineData("""{"<=": [{"var": "days"}, 3]}""", """{"days": 3}""", true)]
    public void Evaluate_NumericComparison(string conditionJson, string contextJson, bool expected)
        => WorkflowConditionEvaluator.Evaluate(Condition(conditionJson), Context(contextJson)).ShouldBe(expected);

    [Fact]
    public void Evaluate_In_MatchesAMemberOfAnArray()
    {
        JsonElement condition = Condition("""{"in": [{"var": "leaveTypeCode"}, ["MATERNITY", "SABBATICAL"]]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"leaveTypeCode": "MATERNITY"}""")).ShouldBeTrue();
        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"leaveTypeCode": "CASUAL"}""")).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_And_RequiresEveryOperandTrue()
    {
        JsonElement condition = Condition(
            """{"and": [{">": [{"var": "days"}, 1]}, {"==": [{"var": "leaveTypeCode"}, "CASUAL"]}]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"days": 5, "leaveTypeCode": "CASUAL"}""")).ShouldBeTrue();
        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"days": 5, "leaveTypeCode": "SICK"}""")).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_Or_RequiresAtLeastOneOperandTrue()
    {
        JsonElement condition = Condition(
            """{"or": [{"==": [{"var": "leaveTypeCode"}, "CASUAL"]}, {"==": [{"var": "leaveTypeCode"}, "SICK"]}]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"leaveTypeCode": "SICK"}""")).ShouldBeTrue();
        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"leaveTypeCode": "MATERNITY"}""")).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_VarOnAMissingPath_ResolvesToNullRatherThanThrowing()
    {
        JsonElement condition = Condition("""{"==": [{"var": "employee.gradeRank"}, 5]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("{}")).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_NestedVarPath_ReadsThroughAnObject()
    {
        JsonElement condition = Condition("""{"==": [{"var": "employee.gradeRank"}, 5]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("""{"employee": {"gradeRank": 5}}""")).ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_UnknownOperator_FailsClosedRatherThanThrowing()
    {
        JsonElement condition = Condition("""{"someUnsupportedOperator": [1, 2]}""");

        WorkflowConditionEvaluator.Evaluate(condition, Context("{}")).ShouldBeFalse();
    }

    private static JsonElement Condition(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static JsonElement Context(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
