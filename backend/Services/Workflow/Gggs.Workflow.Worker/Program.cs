using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Contracts;
using Gggs.Workflow.Application.Snapshots;
using Gggs.Workflow.Application.Workflows;
using Gggs.Workflow.Contracts;
using Gggs.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

/*
 * Workflow's background half.
 *
 * Two responsibilities, the same split every other service's Worker makes. First, this process
 * drains Workflow's own outbox (WorkflowApprovedIntegrationEvent, WorkflowRejectedIntegrationEvent -
 * without this project, a source service like Time would never hear back that its leave request
 * was approved). Second, it is where two subscriptions land: StartWorkflowIntegrationEvent (any
 * service, asking Workflow to begin an approval - architecture.md §13.3 step 1) and Core HR's
 * EmployeeAssignmentChanged/Exited events (keeping ref.EmployeeSnapshot current - §5.2).
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "workflow-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<WorkflowDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("WorkflowDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<WorkflowDbContext>());

builder.Services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
builder.Services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
builder.Services.AddScoped<IWorkflowTaskRepository, WorkflowTaskRepository>();
builder.Services.AddScoped<IDelegationRepository, DelegationRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();

builder.Services.AddScoped<ApproverResolver>();
builder.Services.AddScoped<DelegationResolver>();
builder.Services.AddScoped<WorkflowEngine>();

builder.Services.AddGggsMessaging<WorkflowDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<StartWorkflowIntegrationEvent, StartWorkflowConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, EmployeeSnapshotConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, EmployeeSnapshotConsumer>();

builder.Services.AddGggsEventSubscriber<WorkflowDbContext>();

// Drains Workflow's own outbox (WorkflowApproved/RejectedIntegrationEvent), so the API never talks
// to the broker directly.
builder.Services.AddGggsOutboxRelay<WorkflowDbContext>();

IHost host = builder.Build();
await host.RunAsync();
