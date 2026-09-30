using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Contracts;
using Gggs.Payroll.Application.Snapshots;
using Gggs.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

/*
 * Payroll's background half.
 *
 * One responsibility so far, the same shape every other service's Worker starts from: keeping
 * ref.EmployeeSnapshot current from Core HR's own EmployeeAssignmentChanged/Exited events
 * (architecture.md §5.2), so salary assignment can confirm an employee exists and is not exited
 * without a synchronous call back to Core HR on every request. Drains Payroll's own outbox too,
 * even though nothing publishes into it yet in this v1 slice - the relay costs nothing idle, and
 * every other service's Worker keeps it running unconditionally rather than adding it only once
 * the first event exists to relay.
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "payroll-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<PayrollDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PayrollDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PayrollDbContext>());

builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();

builder.Services.AddGggsMessaging<PayrollDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, EmployeeSnapshotConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, EmployeeSnapshotConsumer>();

builder.Services.AddGggsEventSubscriber<PayrollDbContext>();

// Drains Payroll's own outbox, so the API never talks to the broker directly.
builder.Services.AddGggsOutboxRelay<PayrollDbContext>();

IHost host = builder.Build();
await host.RunAsync();
