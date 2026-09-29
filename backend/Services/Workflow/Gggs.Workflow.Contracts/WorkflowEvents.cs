using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Workflow.Contracts;

/*
 * Workflow's published contract: a command a source service sends to start an approval, and the
 * two events Workflow sends back as it runs. architecture.md §13.3's own step numbers are noted
 * below each type, so the runtime flow can be read against this file directly.
 */

/// <summary>
/// §13.3 step 1: a source service (Time, for a leave request; Spend, for an expense claim; ...)
/// asks Workflow to start an approval, in the same transaction that saved the record being
/// approved - staged to the source service's own outbox, never called synchronously.
/// <para>
/// <see cref="ContextJson"/> is a JSON object of whatever the source service decided the
/// definition's steps might need to evaluate <c>enterIf</c> or resolve approvers against - "days",
/// "leaveTypeCode", "employee.gradeRank" in the architecture doc's own leave example. Workflow
/// does not validate its shape beyond "valid JSON object"; a condition referencing a field the
/// context does not have simply evaluates to <see langword="null"/> for that lookup (see
/// <c>WorkflowConditionEvaluator.ReadVar</c>), which every comparison operator treats as false.
/// </para>
/// </summary>
[MessageType("gggs.workflow.instance.start-requested")]
public sealed record StartWorkflowIntegrationEvent(
    string DefinitionCode,
    string EntityType,
    Guid EntityId,
    string SourceService,
    Guid InitiatorUserId,
    Guid SubjectEmployeeId,
    string ContextJson,

    /// <summary>Duplicated from the envelope, same reasoning as <c>AuditEntryIntegrationEvent</c>'s
    /// own remarks - a handler never sees the envelope, and the instance's own timeline needs this
    /// to tie back to the request that started it.</summary>
    string? CorrelationId) : IntegrationEvent;

/// <summary>
/// §13.3 step 4: every step approved (or the one step that needed to). The source service consumes
/// this to move its own record forward - <c>LeaveRequest.Approve()</c>, a ledger debit, attendance
/// days marked, in the architecture doc's own leave example.
/// </summary>
[MessageType("gggs.workflow.instance.approved")]
public sealed record WorkflowApprovedIntegrationEvent(
    string EntityType, Guid EntityId, Guid InstanceId, Guid FinalApproverUserId) : IntegrationEvent;

/// <summary>Symmetric to <see cref="WorkflowApprovedIntegrationEvent"/> - a step rejected the
/// instance outright, and the source service needs to know why.</summary>
[MessageType("gggs.workflow.instance.rejected")]
public sealed record WorkflowRejectedIntegrationEvent(
    string EntityType, Guid EntityId, Guid InstanceId, Guid RejectedByUserId, string? Comment) : IntegrationEvent;
