using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Workflow.Application.Snapshots;
using Gggs.Workflow.Application.Workflows;
using Gggs.Workflow.Contracts;
using Gggs.Workflow.Domain.Snapshots;
using Gggs.Workflow.Domain.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.Workflow.UnitTests;

public sealed class WorkflowEngineTests
{
    private const string SingleStepDefinition = """
        {
          "steps": [
            { "key": "manager", "name": "Reporting manager", "approvers": { "type": "SpecificUser", "userId": "USER_ID_PLACEHOLDER" } }
          ]
        }
        """;

    private static readonly Guid SubjectEmployeeId = Guid.NewGuid();
    private static readonly Guid InitiatorUserId = Guid.NewGuid();
    private static readonly Guid ApproverUserId = Guid.NewGuid();
    private static readonly Guid EntityId = Guid.NewGuid();

    [Fact]
    public async Task StartAsync_WithNoPublishedVersion_ThrowsADomainRuleException()
    {
        IWorkflowDefinitionRepository definitions = Substitute.For<IWorkflowDefinitionRepository>();
        definitions.GetByCodeAsync("LEAVE_REQUEST", Arg.Any<CancellationToken>())
            .Returns(WorkflowDefinition.Create("LEAVE_REQUEST", "Leave request", "LEAVE_REQUEST"));
        definitions.GetPublishedVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinitionVersion?)null);

        WorkflowEngine engine = BuildEngine(definitions: definitions);

        await Should.ThrowAsync<DomainRuleException>(() => engine.StartAsync(StartRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartAsync_WithAnAlreadyRunningInstanceForTheEntity_ThrowsADomainRuleException()
    {
        WorkflowDefinition definition = WorkflowDefinition.Create("LEAVE_REQUEST", "Leave request", "LEAVE_REQUEST");
        WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(definition.Id, 1, DefinitionJson());
        version.Publish(DateTime.UtcNow);

        IWorkflowDefinitionRepository definitions = Substitute.For<IWorkflowDefinitionRepository>();
        definitions.GetByCodeAsync("LEAVE_REQUEST", Arg.Any<CancellationToken>()).Returns(definition);
        definitions.GetPublishedVersionAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(version);

        IWorkflowInstanceRepository instances = Substitute.For<IWorkflowInstanceRepository>();
        instances.GetRunningForEntityAsync("LEAVE_REQUEST", EntityId, Arg.Any<CancellationToken>())
            .Returns(WorkflowInstance.Start(
                version.Id, "LEAVE_REQUEST", EntityId, "time", InitiatorUserId, SubjectEmployeeId, "{}", null, "manager", DateTime.UtcNow));

        WorkflowEngine engine = BuildEngine(definitions: definitions, instances: instances);

        await Should.ThrowAsync<DomainRuleException>(() => engine.StartAsync(StartRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartAsync_WithAOneStepDefinition_CreatesOneTaskForTheResolvedApprover()
    {
        (WorkflowEngine engine, IWorkflowInstanceRepository instances, IWorkflowTaskRepository tasks) = BuildEngineForSingleStep();

        WorkflowInstance instance = await engine.StartAsync(StartRequest(), TestContext.Current.CancellationToken);

        instance.Status.ShouldBe(WorkflowInstanceStatus.Running);
        instance.CurrentStepKey.ShouldBe("manager");
        instances.Received(1).Add(Arg.Any<WorkflowInstance>());
        tasks.Received(1).Add(Arg.Is<WorkflowTask>(t => t.AssigneeUserId == ApproverUserId && t.StepKey == "manager"));
    }

    [Fact]
    public async Task ActAsync_ApprovingTheOnlyStep_CompletesTheInstanceAndPublishesApproved()
    {
        (WorkflowEngine engine, IWorkflowInstanceRepository instances, IWorkflowTaskRepository tasks,
            _, IIntegrationEventPublisher publisher) = BuildEngineForSingleStepWithHandles();

        WorkflowInstance instance = await engine.StartAsync(StartRequest(), TestContext.Current.CancellationToken);
        WorkflowTask task = WorkflowTask.Create(instance.Id, "manager", ApproverUserId, null, null);

        tasks.GetAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);
        instances.GetAsync(instance.Id, Arg.Any<CancellationToken>()).Returns(instance);
        tasks.GetForInstanceAsync(instance.Id, Arg.Any<CancellationToken>()).Returns([task]);
        tasks.GetPendingForStepAsync(instance.Id, "manager", Arg.Any<CancellationToken>()).Returns([task]);

        await engine.ActAsync(task.Id, ApproverUserId, WorkflowTaskStatus.Approved, comment: "Looks good", TestContext.Current.CancellationToken);

        task.Status.ShouldBe(WorkflowTaskStatus.Approved);
        instance.Status.ShouldBe(WorkflowInstanceStatus.Approved);
        publisher.Received(1).Publish(Arg.Is<WorkflowApprovedIntegrationEvent>(
            e => e.InstanceId == instance.Id && e.FinalApproverUserId == ApproverUserId), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task ActAsync_RejectingATask_CompletesTheInstanceAsRejectedAndPublishesRejected()
    {
        (WorkflowEngine engine, IWorkflowInstanceRepository instances, IWorkflowTaskRepository tasks,
            _, IIntegrationEventPublisher publisher) = BuildEngineForSingleStepWithHandles();

        WorkflowInstance instance = await engine.StartAsync(StartRequest(), TestContext.Current.CancellationToken);
        WorkflowTask task = WorkflowTask.Create(instance.Id, "manager", ApproverUserId, null, null);

        tasks.GetAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);
        instances.GetAsync(instance.Id, Arg.Any<CancellationToken>()).Returns(instance);
        tasks.GetPendingForStepAsync(instance.Id, "manager", Arg.Any<CancellationToken>()).Returns([task]);

        await engine.ActAsync(task.Id, ApproverUserId, WorkflowTaskStatus.Rejected, comment: "Not approved", TestContext.Current.CancellationToken);

        task.Status.ShouldBe(WorkflowTaskStatus.Rejected);
        instance.Status.ShouldBe(WorkflowInstanceStatus.Rejected);
        publisher.Received(1).Publish(Arg.Is<WorkflowRejectedIntegrationEvent>(
            e => e.InstanceId == instance.Id && e.RejectedByUserId == ApproverUserId), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task ActAsync_ByAUserTheTaskIsNotAssignedTo_ThrowsADomainRuleException()
    {
        WorkflowTask task = WorkflowTask.Create(Guid.NewGuid(), "manager", ApproverUserId, null, null);

        IWorkflowTaskRepository tasks = Substitute.For<IWorkflowTaskRepository>();
        tasks.GetAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        WorkflowEngine engine = BuildEngine(tasks: tasks);

        await Should.ThrowAsync<DomainRuleException>(() => engine.ActAsync(
            task.Id, actorUserId: Guid.NewGuid(), WorkflowTaskStatus.Approved, comment: null, TestContext.Current.CancellationToken));
    }

    private static (WorkflowEngine Engine, IWorkflowInstanceRepository Instances, IWorkflowTaskRepository Tasks) BuildEngineForSingleStep()
    {
        (WorkflowEngine engine, IWorkflowInstanceRepository instances, IWorkflowTaskRepository tasks, _, _) = BuildEngineForSingleStepWithHandles();
        return (engine, instances, tasks);
    }

    private static (
        WorkflowEngine Engine,
        IWorkflowInstanceRepository Instances,
        IWorkflowTaskRepository Tasks,
        IWorkflowDefinitionRepository Definitions,
        IIntegrationEventPublisher Publisher) BuildEngineForSingleStepWithHandles()
    {
        WorkflowDefinition definition = WorkflowDefinition.Create("LEAVE_REQUEST", "Leave request", "LEAVE_REQUEST");
        WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(definition.Id, 1, DefinitionJson());
        version.Publish(DateTime.UtcNow);

        IWorkflowDefinitionRepository definitions = Substitute.For<IWorkflowDefinitionRepository>();
        definitions.GetByCodeAsync("LEAVE_REQUEST", Arg.Any<CancellationToken>()).Returns(definition);
        definitions.GetPublishedVersionAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(version);
        definitions.GetPublishedVersionAsync(Guid.Empty, Arg.Any<CancellationToken>()).Returns(version);
        definitions.GetVersionAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        IWorkflowInstanceRepository instances = Substitute.For<IWorkflowInstanceRepository>();
        instances.GetRunningForEntityAsync("LEAVE_REQUEST", EntityId, Arg.Any<CancellationToken>()).Returns((WorkflowInstance?)null);

        IWorkflowTaskRepository tasks = Substitute.For<IWorkflowTaskRepository>();
        IIntegrationEventPublisher publisher = Substitute.For<IIntegrationEventPublisher>();

        WorkflowEngine engine = BuildEngine(definitions, instances, tasks, publisher: publisher);
        return (engine, instances, tasks, definitions, publisher);
    }

    private static WorkflowEngine BuildEngine(
        IWorkflowDefinitionRepository? definitions = null,
        IWorkflowInstanceRepository? instances = null,
        IWorkflowTaskRepository? tasks = null,
        IIntegrationEventPublisher? publisher = null)
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();

        return new WorkflowEngine(
            definitions ?? Substitute.For<IWorkflowDefinitionRepository>(),
            instances ?? Substitute.For<IWorkflowInstanceRepository>(),
            tasks ?? Substitute.For<IWorkflowTaskRepository>(),
            new ApproverResolver(snapshots, NullLogger<ApproverResolver>.Instance),
            new DelegationResolver(Substitute.For<IDelegationRepository>()),
            publisher ?? Substitute.For<IIntegrationEventPublisher>(),
            Substitute.For<IUnitOfWork>(),
            TimeProvider.System,
            NullLogger<WorkflowEngine>.Instance);
    }

    private static StartWorkflowIntegrationEvent StartRequest() => new(
        "LEAVE_REQUEST", "LEAVE_REQUEST", EntityId, "time", InitiatorUserId, SubjectEmployeeId, "{}", CorrelationId: null);

    private static string DefinitionJson() => SingleStepDefinition.Replace("USER_ID_PLACEHOLDER", ApproverUserId.ToString());
}
