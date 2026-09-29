using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Workflow.Application.Snapshots;
using Gggs.Workflow.Application.Workflows;
using Gggs.Workflow.Domain.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.Workflow.UnitTests;

public sealed class ApproverResolverTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ManagerEmployeeId = Guid.NewGuid();
    private static readonly Guid ManagerUserId = Guid.NewGuid();
    private static readonly Guid SkipLevelManagerEmployeeId = Guid.NewGuid();
    private static readonly Guid SkipLevelManagerUserId = Guid.NewGuid();

    [Fact]
    public async Task ResolveAsync_ReportingManagerLevel1_ReturnsTheDirectManagersUser()
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, ManagerEmployeeId, null));
        snapshots.GetAsync(ManagerEmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(ManagerEmployeeId, null, ManagerUserId));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.ReportingManager, Level: 1),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBe([ManagerUserId]);
    }

    [Fact]
    public async Task ResolveAsync_ReportingManagerLevel2_WalksTwoStepsUpTheChain()
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, ManagerEmployeeId, ManagerUserId));
        snapshots.GetAsync(ManagerEmployeeId, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ManagerEmployeeId, SkipLevelManagerEmployeeId, ManagerUserId));
        snapshots.GetAsync(SkipLevelManagerEmployeeId, Arg.Any<CancellationToken>())
            .Returns(Snapshot(SkipLevelManagerEmployeeId, null, SkipLevelManagerUserId));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.ReportingManager, Level: 2),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBe([SkipLevelManagerUserId]);
    }

    [Fact]
    public async Task ResolveAsync_NoSnapshotForTheEmployee_ResolvesToNoApproversRatherThanThrowing()
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns((EmployeeSnapshot?)null);

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.ReportingManager, Level: 1),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ManagerChainCycle_StopsRatherThanLoopingForever()
    {
        // Employee's manager is the employee itself - a data error, but the resolver must not hang.
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, EmployeeId, ManagerUserId));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.ReportingManager, Level: 5),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_SpecificUser_ReturnsExactlyThatUser()
    {
        Guid specificUserId = Guid.NewGuid();
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, null, null));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.SpecificUser, UserId: specificUserId),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBe([specificUserId]);
    }

    [Fact]
    public async Task ResolveAsync_RoleRule_ThrowsNotSupportedRatherThanSilentlyResolvingToNobody()
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        await Should.ThrowAsync<DomainRuleException>(() => resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.Role, RoleTemplate: "HR_MANAGER"),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveAsync_WhenTheResolvedApproverIsTheInitiator_RemovesThemForSegregationOfDuties()
    {
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, ManagerEmployeeId, null));
        snapshots.GetAsync(ManagerEmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(ManagerEmployeeId, null, ManagerUserId));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        // The manager is, unusually, also the one who submitted the request on the employee's behalf.
        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.ReportingManager, Level: 1),
            EmployeeId,
            initiatorUserId: ManagerUserId,
            TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WhenTheResolvedApproverIsTheSubjectsOwnUser_RemovesThemForSegregationOfDuties()
    {
        // SpecificUser happens to name the subject employee's own linked user - self-approval.
        Guid subjectUserId = Guid.NewGuid();
        IEmployeeSnapshotRepository snapshots = Substitute.For<IEmployeeSnapshotRepository>();
        snapshots.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Snapshot(EmployeeId, null, subjectUserId));

        ApproverResolver resolver = new(snapshots, NullLogger<ApproverResolver>.Instance);

        IReadOnlyList<Guid> result = await resolver.ResolveAsync(
            new WorkflowApproverRuleModel(WorkflowApproverRuleType.SpecificUser, UserId: subjectUserId),
            EmployeeId,
            initiatorUserId: Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    private static EmployeeSnapshot Snapshot(Guid employeeId, Guid? managerEmployeeId, Guid? userId) => EmployeeSnapshot.Create(
        employeeId,
        employeeCode: "EMP-" + employeeId.ToString("N")[..8],
        displayName: "Test Employee",
        status: "Confirmed",
        userId,
        legalEntityId: Guid.NewGuid(),
        branchId: Guid.NewGuid(),
        departmentId: Guid.NewGuid(),
        designationId: Guid.NewGuid(),
        gradeId: null,
        managerEmployeeId,
        version: 1);
}
