using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gggs.BuildingBlocks.Domain.Exceptions;

namespace Gggs.Modules.Workflow.Domain;

/// <summary>Who approves, relative to the requester or by name.</summary>
public enum ApproverKind
{
    ReportingManager = 1,
    SkipLevelManager = 2,
    DepartmentHead = 3,
    HrBusinessPartner = 4,
    Employee = 5,
}

/// <summary>How several approvers in one stage decide: everyone, or the first to answer.</summary>
public enum StageMode
{
    All = 1,
    Any = 2,
}

public enum ConditionOperator
{
    Equals = 1,
    NotEquals = 2,
    GreaterThan = 3,
    GreaterThanOrEqual = 4,
    LessThan = 5,
    LessThanOrEqual = 6,
    In = 7,
}

public sealed record ApproverRule(ApproverKind Kind, Guid? EmployeeId = null);

/// <summary>
/// When a stage applies. A stage whose condition is false is skipped for that request - the way a
/// definition says "finance approves only above 50,000".
/// </summary>
public sealed record StageCondition(string Field, ConditionOperator Operator, string Value)
{
    /// <summary>
    /// Evaluates against the request's fields. A field the request does not carry makes the
    /// condition false: fields come from the owning module's code, not from the requester, so a
    /// missing one means "not applicable" rather than an attempt to dodge a stage.
    /// </summary>
    public bool IsMet(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (!fields.TryGetValue(Field, out string? actual) || actual is null)
        {
            return false;
        }

        if (Operator == ConditionOperator.In)
        {
            return Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(actual.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        if (Operator is ConditionOperator.Equals or ConditionOperator.NotEquals)
        {
            bool equal = TryNumber(actual, out decimal left) && TryNumber(Value, out decimal right)
                ? left == right
                : string.Equals(actual.Trim(), Value.Trim(), StringComparison.OrdinalIgnoreCase);
            return Operator == ConditionOperator.Equals ? equal : !equal;
        }

        if (!TryNumber(actual, out decimal number) || !TryNumber(Value, out decimal threshold))
        {
            return false;
        }

        return Operator switch
        {
            ConditionOperator.GreaterThan => number > threshold,
            ConditionOperator.GreaterThanOrEqual => number >= threshold,
            ConditionOperator.LessThan => number < threshold,
            ConditionOperator.LessThanOrEqual => number <= threshold,
            _ => false,
        };
    }

    private static bool TryNumber(string text, out decimal value)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// One step of a definition. Stages run in order; the approvers inside one stage work in parallel,
/// deciding together (<see cref="StageMode.All"/>) or by the first answer (<see cref="StageMode.Any"/>).
/// </summary>
public sealed record WorkflowStage(
    string Name,
    StageMode Mode,
    IReadOnlyList<ApproverRule> Approvers,
    StageCondition? Condition = null,
    int? SlaHours = null,
    ApproverRule? EscalateTo = null);

/// <summary>Stage lists and request fields are stored as JSON: read and written whole, never queried into.</summary>
public static class StageJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(IReadOnlyList<WorkflowStage> stages) => JsonSerializer.Serialize(stages, Options);

    public static IReadOnlyList<WorkflowStage> Read(string json)
        => JsonSerializer.Deserialize<List<WorkflowStage>>(json, Options) ?? [];

    public static string WriteFields(IReadOnlyDictionary<string, string> fields) => JsonSerializer.Serialize(fields, Options);

    public static IReadOnlyDictionary<string, string> ReadFields(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? [];

    /// <summary>The rules every definition's stages must satisfy.</summary>
    public static void Validate(IReadOnlyList<WorkflowStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        if (stages.Count is 0 or > 10)
        {
            throw new DomainRuleException("WORKFLOW_STAGE_COUNT", "A workflow has between one and ten stages.");
        }

        foreach (WorkflowStage stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name) || stage.Name.Length > 100)
            {
                throw new DomainRuleException("WORKFLOW_STAGE_NAME", "Every stage needs a name of up to 100 characters.");
            }

            if (stage.Approvers is null || stage.Approvers.Count is 0 or > 10)
            {
                throw new DomainRuleException("WORKFLOW_STAGE_APPROVERS", $"Stage '{stage.Name}' needs between one and ten approvers.");
            }

            IEnumerable<ApproverRule> rules = stage.EscalateTo is null ? stage.Approvers : stage.Approvers.Append(stage.EscalateTo);
            foreach (ApproverRule rule in rules)
            {
                if (!Enum.IsDefined(rule.Kind))
                {
                    throw new DomainRuleException("WORKFLOW_APPROVER_KIND", $"Stage '{stage.Name}' has an unknown approver kind.");
                }

                if (rule.Kind == ApproverKind.Employee && rule.EmployeeId is null)
                {
                    throw new DomainRuleException("WORKFLOW_APPROVER_EMPLOYEE", $"Stage '{stage.Name}' names an employee approver without choosing one.");
                }
            }

            if (stage.SlaHours is < 1 or > 720)
            {
                throw new DomainRuleException("WORKFLOW_STAGE_SLA", $"Stage '{stage.Name}' has an SLA outside 1 to 720 hours.");
            }

            if (stage.Condition is { } condition
                && (string.IsNullOrWhiteSpace(condition.Field) || string.IsNullOrWhiteSpace(condition.Value) || !Enum.IsDefined(condition.Operator)))
            {
                throw new DomainRuleException("WORKFLOW_STAGE_CONDITION", $"Stage '{stage.Name}' has an incomplete condition.");
            }
        }
    }
}
