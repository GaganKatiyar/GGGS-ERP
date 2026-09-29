using Gggs.Workflow.Domain.Workflows;

namespace Gggs.Workflow.Api.Contracts;

public sealed record WorkflowDefinitionResponse(Guid Id, string Code, string Name, string EntityType, bool IsSystem);

public sealed record CreateWorkflowDefinitionBody(string Code, string Name, string EntityType);

public sealed record WorkflowDefinitionVersionResponse(
    Guid Id, Guid DefinitionId, int Version, WorkflowVersionStatus Status, string DefinitionJson, DateTime? PublishedDate);

public sealed record CreateWorkflowDefinitionVersionBody(string DefinitionJson);

public sealed record UpdateWorkflowDefinitionVersionBody(string DefinitionJson);

public sealed record WorkflowInstanceResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string SourceService,
    Guid InitiatorUserId,
    Guid SubjectEmployeeId,
    WorkflowInstanceStatus Status,
    string? CurrentStepKey,
    DateTime StartedDate,
    DateTime? CompletedDate);

public sealed record WorkflowActionLogItem(
    Guid Id, string Action, Guid ActorUserId, Guid? OnBehalfOfUserId, string? Comment, DateTime OccurredDate);

public sealed record WorkflowTaskItem(
    Guid Id,
    Guid InstanceId,
    string EntityType,
    Guid EntityId,
    string SourceService,
    string StepKey,
    Guid? OriginalAssigneeUserId,
    WorkflowTaskStatus Status,
    DateTime? DueDate);

public sealed record ActOnWorkflowTaskBody(string? Comment);

public sealed record DelegationResponse(Guid Id, Guid FromUserId, Guid ToUserId, DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record CreateDelegationBody(Guid ToUserId, DateOnly StartDate, DateOnly EndDate, string? Reason);
