using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Workflow.Contracts;
using Gggs.Workflow.Domain.Workflows;
using Microsoft.Extensions.Logging;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// Runs a <see cref="WorkflowInstance"/> through its <see cref="WorkflowDefinitionVersion"/>'s
/// steps - architecture.md §13.3's runtime flow, steps 1 through 4. Delegation resolution
/// (step 2's "applies active delegations") and SLA scanning (step 5) are their own pieces
/// (<see cref="DelegationResolver"/>, the Worker's recurring job); this class owns starting an
/// instance, resolving one step's tasks, and reacting to a task being acted on.
/// </summary>
public sealed class WorkflowEngine(
    IWorkflowDefinitionRepository definitions,
    IWorkflowInstanceRepository instances,
    IWorkflowTaskRepository tasks,
    ApproverResolver approverResolver,
    DelegationResolver delegationResolver,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<WorkflowEngine> logger)
{
    private static readonly JsonSerializerOptions ContextJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowInstance> StartAsync(StartWorkflowIntegrationEvent request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await definitions.GetByCodeAsync(request.DefinitionCode, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinition), request.DefinitionCode);

        WorkflowDefinitionVersion version = await definitions.GetPublishedVersionAsync(definition.Id, cancellationToken)
            ?? throw new DomainRuleException(
                "WORKFLOW_DEFINITION_NOT_PUBLISHED",
                $"Definition '{request.DefinitionCode}' has no published version.");

        WorkflowInstance? existing = await instances.GetRunningForEntityAsync(request.EntityType, request.EntityId, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException(
                "WORKFLOW_INSTANCE_ALREADY_RUNNING",
                $"{request.EntityType} {request.EntityId} already has a running workflow instance.");
        }

        WorkflowDefinitionModel model = Deserialize(version.DefinitionJson);
        JsonElement context = JsonSerializer.SerializeToElement(
            JsonSerializer.Deserialize<JsonElement>(request.ContextJson), ContextJsonOptions);

        WorkflowStepModel? firstStep = FindNextStep(model, currentStepIndex: -1, context);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (firstStep is null)
        {
            // Every step's enterIf evaluated false: nothing to approve, so the instance completes
            // immediately rather than sitting "Running" with no task anyone can ever act on.
            WorkflowInstance emptyInstance = WorkflowInstance.Start(
                version.Id, request.EntityType, request.EntityId, request.SourceService,
                request.InitiatorUserId, request.SubjectEmployeeId, request.ContextJson,
                request.CorrelationId, firstStepKey: "none", now);

            emptyInstance.Complete(WorkflowInstanceStatus.Approved, now);
            instances.Add(emptyInstance);
            PublishApproved(emptyInstance, finalApproverUserId: request.InitiatorUserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return emptyInstance;
        }

        WorkflowInstance instance = WorkflowInstance.Start(
            version.Id, request.EntityType, request.EntityId, request.SourceService,
            request.InitiatorUserId, request.SubjectEmployeeId, request.ContextJson,
            request.CorrelationId, firstStep.Key, now);

        instances.Add(instance);
        instances.Add(WorkflowActionLog.Record(
            instance.Id, taskId: null, "Started", request.InitiatorUserId, onBehalfOfUserId: null, comment: null, now));

        await CreateTasksForStepAsync(instance, firstStep, context, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Started workflow instance {InstanceId} for {EntityType} {EntityId} at step {StepKey}.",
            instance.Id, request.EntityType, request.EntityId, firstStep.Key);

        return instance;
    }

    public async Task ActAsync(
        Guid taskId, Guid actorUserId, WorkflowTaskStatus outcome, string? comment, CancellationToken cancellationToken = default)
    {
        WorkflowTask task = await tasks.GetAsync(taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTask), taskId);

        Guard(task.AssigneeUserId == actorUserId, "WORKFLOW_TASK_NOT_ASSIGNED", "This task is not assigned to you.");

        WorkflowInstance instance = await instances.GetAsync(task.InstanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowInstance), task.InstanceId);

        Guard(instance.Status == WorkflowInstanceStatus.Running, "WORKFLOW_INSTANCE_NOT_RUNNING", "This instance is not running.");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        task.Act(outcome, comment, now);

        instances.Add(WorkflowActionLog.Record(
            instance.Id, task.Id, outcome.ToString(), actorUserId, task.OriginalAssigneeUserId, comment, now));

        if (outcome == WorkflowTaskStatus.Rejected || outcome == WorkflowTaskStatus.SentBack)
        {
            await CancelSiblingTasksAsync(instance.Id, task.StepKey, task.Id, now, cancellationToken);
            instance.Complete(WorkflowInstanceStatus.Rejected, now);
            PublishRejected(instance, actorUserId, comment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        WorkflowDefinitionVersion version = await definitions.GetVersionAsync(instance.DefinitionVersionId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowDefinitionVersion), instance.DefinitionVersionId);

        WorkflowDefinitionModel model = Deserialize(version.DefinitionJson);
        int currentIndex = model.Steps.ToList().FindIndex(s => s.Key == task.StepKey);
        WorkflowStepModel currentStep = model.Steps[currentIndex];

        bool stepResolved = await IsStepResolvedAsync(instance.Id, currentStep, cancellationToken);

        if (!stepResolved)
        {
            // An "Any" step is already done as soon as one approver acts; an "All" step waits for
            // every sibling task to reach a terminal state before the instance moves on.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        await CancelSiblingTasksAsync(instance.Id, task.StepKey, task.Id, now, cancellationToken);

        JsonElement context = JsonSerializer.Deserialize<JsonElement>(instance.ContextJson);
        WorkflowStepModel? nextStep = FindNextStep(model, currentIndex, context);

        if (nextStep is null)
        {
            instance.Complete(WorkflowInstanceStatus.Approved, now);
            PublishApproved(instance, actorUserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        instance.AdvanceTo(nextStep.Key);
        await CreateTasksForStepAsync(instance, nextStep, context, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid instanceId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await instances.GetAsync(instanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowInstance), instanceId);

        Guard(instance.InitiatorUserId == actorUserId, "WORKFLOW_INSTANCE_NOT_INITIATOR", "Only the initiator can cancel this instance.");
        Guard(instance.Status == WorkflowInstanceStatus.Running, "WORKFLOW_INSTANCE_NOT_RUNNING", "This instance is not running.");

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (instance.CurrentStepKey is { } stepKey)
        {
            await CancelSiblingTasksAsync(instance.Id, stepKey, actedTaskId: null, now, cancellationToken);
        }

        instance.Complete(WorkflowInstanceStatus.Cancelled, now);
        instances.Add(WorkflowActionLog.Record(instance.Id, taskId: null, "Cancelled", actorUserId, onBehalfOfUserId: null, comment: null, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsStepResolvedAsync(Guid instanceId, WorkflowStepModel step, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowTask> stepTasks = await tasks.GetForInstanceAsync(instanceId, cancellationToken);
        List<WorkflowTask> forStep = stepTasks.Where(t => t.StepKey == step.Key).ToList();

        return step.Mode == WorkflowApprovalMode.Any
            ? forStep.Any(t => t.Status == WorkflowTaskStatus.Approved)
            : forStep.All(t => t.Status == WorkflowTaskStatus.Approved);
    }

    private async Task CreateTasksForStepAsync(
        WorkflowInstance instance, WorkflowStepModel step, JsonElement context, DateTime now, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> approvers = await approverResolver.ResolveAsync(
            step.Approvers, instance.SubjectEmployeeId, instance.InitiatorUserId, cancellationToken);

        if (approvers.Count == 0)
        {
            logger.LogWarning(
                "Step {StepKey} of instance {InstanceId} resolved no approvers; the instance will have no way to progress until reassigned.",
                step.Key, instance.Id);
            return;
        }

        DateTime? dueDate = step.SlaHours is { } hours ? now.AddHours(hours) : null;

        foreach (Guid approverUserId in approvers)
        {
            Guid? delegateUserId = await delegationResolver.ResolveDelegateAsync(approverUserId, DateOnly.FromDateTime(now), cancellationToken);
            Guid assignee = delegateUserId ?? approverUserId;

            tasks.Add(WorkflowTask.Create(
                instance.Id, step.Key, assignee, delegateUserId is not null ? approverUserId : null, dueDate));
        }
    }

    private async Task CancelSiblingTasksAsync(
        Guid instanceId, string stepKey, Guid? actedTaskId, DateTime now, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowTask> pending = await tasks.GetPendingForStepAsync(instanceId, stepKey, cancellationToken);

        foreach (WorkflowTask sibling in pending)
        {
            if (sibling.Id != actedTaskId)
            {
                sibling.Cancel(now);
            }
        }
    }

    private static WorkflowStepModel? FindNextStep(WorkflowDefinitionModel model, int currentStepIndex, JsonElement context)
    {
        for (int i = currentStepIndex + 1; i < model.Steps.Count; i++)
        {
            WorkflowStepModel candidate = model.Steps[i];

            if (WorkflowConditionEvaluator.Evaluate(candidate.EnterIf, context))
            {
                return candidate;
            }
        }

        return null;
    }

    private void PublishApproved(WorkflowInstance instance, Guid finalApproverUserId)
        => publisher.Publish(new WorkflowApprovedIntegrationEvent(instance.EntityType, instance.EntityId, instance.Id, finalApproverUserId));

    private void PublishRejected(WorkflowInstance instance, Guid rejectedByUserId, string? comment)
        => publisher.Publish(new WorkflowRejectedIntegrationEvent(instance.EntityType, instance.EntityId, instance.Id, rejectedByUserId, comment));

    private static WorkflowDefinitionModel Deserialize(string definitionJson)
        => JsonSerializer.Deserialize<WorkflowDefinitionModel>(definitionJson, ContextJsonOptions)
            ?? throw new DomainRuleException("WORKFLOW_DEFINITION_INVALID", "The published definition JSON could not be parsed.");

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
