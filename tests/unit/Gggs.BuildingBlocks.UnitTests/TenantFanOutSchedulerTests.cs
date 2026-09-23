using System.Linq.Expressions;
using Gggs.BuildingBlocks.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

/// <summary>
/// Platform-wide schedules fan out into one job per tenant. These tests pin that behaviour, because
/// the alternative - one job looping over tenants - fails badly in ways that only show up in
/// production: one slow tenant delays everyone, and a retry re-runs the tenants that already
/// succeeded.
/// </summary>
public sealed class TenantFanOutSchedulerTests
{
    private static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GlobexTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task FanOut_QueuesOneJobPerActiveTenant()
    {
        IBackgroundJobClient jobClient = Substitute.For<IBackgroundJobClient>();
        TenantFanOutScheduler scheduler = CreateScheduler(jobClient, AcmeTenantId, GlobexTenantId);

        await scheduler.FanOutAsync<IAccrualJob>((job, tenantId) => job.RunAsync(tenantId), TestContext.Current.CancellationToken);

        jobClient.Received(2).Create(Arg.Any<Job>(), Arg.Any<EnqueuedState>());
    }

    [Fact]
    public async Task FanOut_BindsEachTenantIdIntoTheQueuedCall()
    {
        IBackgroundJobClient jobClient = Substitute.For<IBackgroundJobClient>();
        List<Guid> queuedTenantIds = [];

        jobClient
            .Create(Arg.Do<Job>(job => queuedTenantIds.Add((Guid)job.Args[0]!)), Arg.Any<EnqueuedState>())
            .Returns(callInfo => Guid.NewGuid().ToString());

        TenantFanOutScheduler scheduler = CreateScheduler(jobClient, AcmeTenantId, GlobexTenantId);

        await scheduler.FanOutAsync<IAccrualJob>((job, tenantId) => job.RunAsync(tenantId), TestContext.Current.CancellationToken);

        // Each queued job carries its own tenant; none of them run "for whoever queued this".
        queuedTenantIds.ShouldBe([AcmeTenantId, GlobexTenantId], ignoreOrder: true);
    }

    [Fact]
    public async Task FanOut_QueuesNothingWhenNoTenantIsActive()
    {
        IBackgroundJobClient jobClient = Substitute.For<IBackgroundJobClient>();
        TenantFanOutScheduler scheduler = CreateScheduler(jobClient);

        await scheduler.FanOutAsync<IAccrualJob>((job, tenantId) => job.RunAsync(tenantId), TestContext.Current.CancellationToken);

        jobClient.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public void RecurringJobId_IsScopedPerTenant()
    {
        // Two tenants sharing a recurring-job id would mean one tenant's reschedule silently
        // replaces the other's.
        RecurringJobId.For("leave-accrual", AcmeTenantId)
            .ShouldNotBe(RecurringJobId.For("leave-accrual", GlobexTenantId));

        RecurringJobId.For("leave-accrual", AcmeTenantId)
            .ShouldBe("leave-accrual:11111111111111111111111111111111");
    }

    private static TenantFanOutScheduler CreateScheduler(IBackgroundJobClient jobClient, params Guid[] tenantIds)
    {
        IActiveTenantProvider tenants = Substitute.For<IActiveTenantProvider>();
        tenants.GetActiveTenantIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>>(tenantIds));

        return new TenantFanOutScheduler(jobClient, tenants, NullLogger<TenantFanOutScheduler>.Instance);
    }

    /// <summary>Stand-in for a real recurring job, such as nightly leave accrual.</summary>
    public interface IAccrualJob
    {
        Task RunAsync(Guid tenantId);
    }
}
