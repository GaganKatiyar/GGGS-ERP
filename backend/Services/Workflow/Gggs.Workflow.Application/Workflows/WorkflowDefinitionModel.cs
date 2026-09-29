using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gggs.Workflow.Application.Workflows;

/*
 * The shape WorkflowDefinitionVersion.DefinitionJson deserializes into - architecture.md §13.2's
 * own example, trimmed to what v1's engine actually executes: sequential steps only (no parallel
 * branches - that is explicitly Phase 6), Any/All modes (no Quorum(n) yet), and the approver rule
 * types ApproverResolver.cs implements. A definition JSON with a step property this model does not
 * recognize round-trips fine (System.Text.Json ignores unknown members by default) - only the
 * fields the engine reads are modelled here.
 */

public sealed record WorkflowDefinitionModel(
    IReadOnlyList<WorkflowStepModel> Steps,
    WorkflowDefinitionOptionsModel? Options = null);

public sealed record WorkflowStepModel(
    string Key,
    string Name,
    WorkflowApproverRuleModel Approvers,
    WorkflowApprovalMode Mode = WorkflowApprovalMode.Any,
    int? SlaHours = null,
    int? ReminderHours = null,

    /// <summary>JsonLogic condition; step is skipped entirely (falls through to the next one) if
    /// this evaluates false. Absent means "always enter" - see
    /// <see cref="WorkflowConditionEvaluator.Evaluate"/>.</summary>
    JsonElement? EnterIf = null,

    WorkflowTimeoutAction? OnTimeout = null);

/// <summary>
/// Who resolves for a step. <see cref="Type"/> selects which of the other fields apply - modelled
/// as one flat record rather than a discriminated union because the JSON itself is flat
/// (architecture.md §13.2's own examples), and a mismatched combination (e.g. <c>Role</c> with no
/// <see cref="RoleTemplate"/>) is caught by <c>ApproverResolver</c> at resolution time with a
/// specific error, not by deserialization.
/// </summary>
public sealed record WorkflowApproverRuleModel(
    WorkflowApproverRuleType Type,
    int? Level = null,
    string? RoleTemplate = null,
    string? Scope = null,
    Guid? UserId = null,
    Guid? GroupId = null);

public sealed record WorkflowDefinitionOptionsModel(
    bool AllowWithdraw = true,
    bool AllowSendBack = true,
    bool NotifyInitiatorOnEachStep = false);

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowApproverRuleType>))]
public enum WorkflowApproverRuleType
{
    /// <summary>The subject employee's reporting manager, or their manager's manager for
    /// <c>Level</c> 2, and so on - walks <c>EmployeeSnapshot.ManagerEmployeeId</c>.</summary>
    ReportingManager,

    /// <summary>A specific platform role template code (e.g. "HR_MANAGER") - every user holding a
    /// role with that <c>iam.Role.TemplateCode</c>, scoped by <see cref="WorkflowApproverRuleModel.Scope"/>
    /// ("EmployeeBranch", "EmployeeDepartment", "Tenant"; anything else resolves tenant-wide).
    /// Requires an Identity lookup - see <c>ApproverResolver</c>'s own remarks on why this is the
    /// one rule type that is not resolvable from the local snapshot alone.</summary>
    Role,

    SpecificUser,

    ApproverGroup,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowApprovalMode>))]
public enum WorkflowApprovalMode
{
    Any,
    All,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowTimeoutActionKind>))]
public enum WorkflowTimeoutActionKind
{
    Escalate,
    AutoApprove,
    AutoReject,
    Notify,
}

public sealed record WorkflowTimeoutAction(WorkflowTimeoutActionKind Action, WorkflowApproverRuleModel? To = null);
