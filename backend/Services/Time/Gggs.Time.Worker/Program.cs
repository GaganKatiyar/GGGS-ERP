using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Contracts;
using Gggs.Time.Application.Attendance;
using Gggs.Time.Application.Holidays;
using Gggs.Time.Application.Leave;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Infrastructure;
using Gggs.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

/*
 * Time's background half.
 *
 * Two responsibilities, the same split every other service's Worker makes. First, this process
 * drains Time's own outbox (StartWorkflowIntegrationEvent, staged by LeaveRequestService.SubmitAsync
 * in the same transaction as the request - without this project, that command would sit in the
 * table forever). Second, it is where two subscriptions land: WorkflowApproved/RejectedIntegrationEvent
 * (architecture.md §13.3 step 4, reacting to Workflow's own outcome) and Core HR's
 * EmployeeAssignmentChanged/Exited events (keeping ref.EmployeeSnapshot current - §5.2).
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "time-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<TimeDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TimeDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TimeDbContext>());

builder.Services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
builder.Services.AddScoped<ILeaveLedgerRepository, LeaveLedgerRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();
builder.Services.AddScoped<IAttendanceRegularizationRepository, AttendanceRegularizationRepository>();
builder.Services.AddScoped<IAttendanceDayRepository, AttendanceDayRepository>();

builder.Services.AddScoped<LeaveLedgerService>();

builder.Services.AddGggsMessaging<TimeDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, LeaveWorkflowOutcomeConsumer>();
builder.Services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, LeaveWorkflowOutcomeConsumer>();
builder.Services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, AttendanceWorkflowOutcomeConsumer>();
builder.Services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, AttendanceWorkflowOutcomeConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, EmployeeSnapshotConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, EmployeeSnapshotConsumer>();

builder.Services.AddGggsEventSubscriber<TimeDbContext>();

// Drains Time's own outbox (StartWorkflowIntegrationEvent), so the API never talks to the broker
// directly.
builder.Services.AddGggsOutboxRelay<TimeDbContext>();

IHost host = builder.Build();
await host.RunAsync();
