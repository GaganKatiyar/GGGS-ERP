using Gggs.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.Modules.Workflow.Contracts;

public enum WorkflowFieldType
{
    Text = 1,
    Number = 2,
}

/// <summary>A value a request carries that a stage condition can test, e.g. a claim's amount.</summary>
public sealed record WorkflowSubjectField(string Key, string Label, WorkflowFieldType Type);

/// <summary>
/// A kind of record that can go through approval - "policy.publish", later "leave.request".
/// <para>
/// The module that owns the record registers it (<see cref="WorkflowSubjectRegistration"/>), so the
/// definition editor offers only subjects something will actually start, with the fields their
/// conditions may test.
/// </para>
/// </summary>
public sealed record WorkflowSubject(string Code, string Name, string Module, IReadOnlyList<WorkflowSubjectField> Fields);

public static class WorkflowSubjectRegistration
{
    public static IServiceCollection AddWorkflowSubject(this IServiceCollection services, WorkflowSubject subject)
        => services.AddSingleton(subject);
}

/// <summary>What a module asks Workflow to approve.</summary>
public sealed record WorkflowStartRequest
{
    public required string SubjectType { get; init; }

    public required Guid SubjectId { get; init; }

    /// <summary>Shown in the approver's inbox: "Publish Leave Policy v3".</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The person the request is raised by or about. Relative approvers (their manager, their
    /// department head) are resolved from them, and they can never approve their own request.
    /// </summary>
    public Guid? RequesterEmployeeId { get; init; }

    /// <summary>Values stage conditions test. Keys are the subject's registered field keys.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Where in the UI the subject lives, so an approver can open it.</summary>
    public string? Link { get; init; }
}

public enum WorkflowStartOutcome
{
    /// <summary>Approval is under way; the module waits for <see cref="WorkflowApprovedIntegrationEvent"/> or a rejection.</summary>
    Pending = 1,

    /// <summary>
    /// Nothing needed approving - the tenant has no active definition for the subject, or every
    /// stage's condition left it out. The module proceeds now; no event follows.
    /// </summary>
    AutoApproved = 2,
}

public sealed record WorkflowStartResult(WorkflowStartOutcome Outcome, Guid? InstanceId);

/// <summary>Starts approvals. The only way into the Workflow module for other modules (ADR-12).</summary>
public interface IWorkflowStarter
{
    /// <summary>
    /// Starts approval in the caller's transaction: the instance and its first tasks are saved with
    /// whatever the caller saves next. Refused while the same subject already has an open request.
    /// </summary>
    Task<WorkflowStartResult> StartAsync(WorkflowStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the subject's open request, if any - the record it was about went away.</summary>
    Task WithdrawAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default);
}

/// <summary>A task landed in someone's inbox.</summary>
[MessageType("gggs.workflow.task-assigned")]
public sealed record WorkflowTaskAssignedIntegrationEvent(
    Guid TaskId,
    Guid InstanceId,
    string SubjectType,
    Guid SubjectId,
    string Title,
    Guid AssigneeEmployeeId,
    Guid? DelegatedFromEmployeeId,
    DateTime? DueDate,
    string? Link) : IntegrationEvent;

/// <summary>A task went past its SLA and moved up.</summary>
[MessageType("gggs.workflow.task-escalated")]
public sealed record WorkflowTaskEscalatedIntegrationEvent(
    Guid TaskId,
    Guid InstanceId,
    string Title,
    Guid FromEmployeeId,
    Guid ToEmployeeId,
    string? Link) : IntegrationEvent;

/// <summary>Every stage approved. The owning module completes the action it was waiting on.</summary>
[MessageType("gggs.workflow.approved")]
public sealed record WorkflowApprovedIntegrationEvent(
    Guid InstanceId,
    string SubjectType,
    Guid SubjectId,
    string Title,
    Guid? RequesterEmployeeId,
    string? Link) : IntegrationEvent;

/// <summary>An approver said no. The request is closed.</summary>
[MessageType("gggs.workflow.rejected")]
public sealed record WorkflowRejectedIntegrationEvent(
    Guid InstanceId,
    string SubjectType,
    Guid SubjectId,
    string Title,
    Guid? RequesterEmployeeId,
    string? Comment,
    string? Link) : IntegrationEvent;

/// <summary>An approver asked for changes. The requester may resubmit; until then nothing proceeds.</summary>
[MessageType("gggs.workflow.sent-back")]
public sealed record WorkflowSentBackIntegrationEvent(
    Guid InstanceId,
    string SubjectType,
    Guid SubjectId,
    string Title,
    Guid? RequesterEmployeeId,
    string? Comment,
    string? Link) : IntegrationEvent;

/// <summary>The request was withdrawn before a decision.</summary>
[MessageType("gggs.workflow.withdrawn")]
public sealed record WorkflowWithdrawnIntegrationEvent(
    Guid InstanceId,
    string SubjectType,
    Guid SubjectId) : IntegrationEvent;
