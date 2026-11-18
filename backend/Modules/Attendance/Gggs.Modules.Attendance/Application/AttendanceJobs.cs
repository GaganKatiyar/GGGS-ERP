using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Attendance.Application;

/// <summary>Every night, queues one day-closing run per active tenant.</summary>
[PlatformJob("Attendance close: fans out one previous-day close per active tenant.")]
internal sealed class AttendanceCloseSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "attendance-close";

    public Task RunAsync() => fanOut.FanOutAsync<AttendanceCloseJob>((job, tenantId) => job.RunAsync(tenantId));
}

/// <summary>
/// Records yesterday for everyone, so a day nobody punched becomes an absence on record. Recloses
/// the two days before as well: punches imported late from a device land in them.
/// </summary>
internal sealed class AttendanceCloseJob(AttendanceService attendance, IEmployeeDirectory employees, ITenantClock clock, ILogger<AttendanceCloseJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        DateOnly yesterday = (await clock.TodayAsync(CancellationToken.None)).AddDays(-1);
        IReadOnlyList<Guid> people = await employees.ListActiveIdsAsync(null, null, CancellationToken.None);
        int written = await attendance.PersistAsync(people, yesterday.AddDays(-2), yesterday, CancellationToken.None);
        logger.LogInformation("Closed attendance up to {Day} for tenant {TenantId}: {Days} day(s) recorded.", yesterday, tenantId, written);
    }
}

internal sealed class AttendanceJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Hourly: each tenant's "yesterday" turns over at its own midnight; re-closing a day is harmless.
        recurringJobs.AddOrUpdate<AttendanceCloseSweep>(AttendanceCloseSweep.RecurringJobName, sweep => sweep.RunAsync(), "15 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class RegularizeOnApproval(AttendanceService attendance) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == AttendanceService.RegularizationSubjectType)
        {
            await attendance.CompleteRegularizationAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectRegularizationOnRejection(AttendanceService attendance) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == AttendanceService.RegularizationSubjectType)
        {
            await attendance.CompleteRegularizationAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}

/// <summary>Approved leave changes how those days are judged; closed days are recorded again.</summary>
internal sealed class RecloseOnLeaveApproved(AttendanceService attendance) : IIntegrationEventConsumer<LeaveApprovedIntegrationEvent>
{
    public Task HandleAsync(LeaveApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return attendance.PersistAsync([integrationEvent.EmployeeId], integrationEvent.StartDate, integrationEvent.EndDate, cancellationToken);
    }
}

/// <summary>
/// A shift's rules changed: the closed days of the current month (the month yesterday falls in, so
/// the month just ended is still covered on the 1st) are judged again for everyone who worked it.
/// Earlier months are left as recorded - their payroll has normally been run.
/// </summary>
internal sealed class RecloseOnShiftChanged(AttendanceService attendance, IEmployeeDirectory employees, IShiftSchedule schedule, ITenantClock clock)
    : IIntegrationEventConsumer<ShiftChangedIntegrationEvent>
{
    public async Task HandleAsync(ShiftChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        DateOnly yesterday = (await clock.TodayAsync(cancellationToken)).AddDays(-1);
        DateOnly from = new(yesterday.Year, yesterday.Month, 1);

        IReadOnlyList<Guid> people = await employees.ListActiveIdsAsync(null, null, cancellationToken);
        IReadOnlyList<ScheduledDay> days = await schedule.GetAsync(people, from, yesterday, cancellationToken);
        List<Guid> onShift = [.. days.Where(day => day.ShiftId == integrationEvent.ShiftId).Select(day => day.EmployeeId).Distinct()];
        if (onShift.Count > 0)
        {
            await attendance.PersistAsync(onShift, from, yesterday, cancellationToken);
        }
    }
}

internal sealed class RecloseOnLeaveCancelled(AttendanceService attendance) : IIntegrationEventConsumer<LeaveCancelledIntegrationEvent>
{
    public Task HandleAsync(LeaveCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return attendance.PersistAsync([integrationEvent.EmployeeId], integrationEvent.StartDate, integrationEvent.EndDate, cancellationToken);
    }
}
