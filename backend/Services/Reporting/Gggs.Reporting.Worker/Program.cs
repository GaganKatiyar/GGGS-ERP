using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Contracts;
using Gggs.Reporting.Application.Headcount;
using Gggs.Reporting.Infrastructure;
using Gggs.Reporting.Worker;
using Microsoft.EntityFrameworkCore;

/*
 * Reporting's background half.
 *
 * Two responsibilities, the same shape every other service's Worker starts from: keeping
 * rpt.EmployeeRosterFact current from Core HR's own EmployeeAssignmentChanged/Exited events
 * (architecture.md §5.2), and - unlike every other Worker so far - a daily job that turns that
 * roster into HeadcountDailySnapshot fact rows (architecture.md §7.2/§12.2). Drains Reporting's
 * own outbox too, even though nothing publishes into it yet in this v1 slice, for the same reason
 * every other service's Worker keeps it running unconditionally.
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "reporting-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<ReportingDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ReportingDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ReportingDbContext>());

builder.Services.AddScoped<IEmployeeRosterFactRepository, EmployeeRosterFactRepository>();
builder.Services.AddScoped<IHeadcountDailySnapshotRepository, HeadcountDailySnapshotRepository>();
builder.Services.AddScoped<HeadcountSnapshotBuilder>();

builder.Services.AddGggsMessaging<ReportingDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, EmployeeRosterConsumer>();
builder.Services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, EmployeeRosterConsumer>();

builder.Services.AddGggsEventSubscriber<ReportingDbContext>();

// Drains Reporting's own outbox, so the API never talks to the broker directly.
builder.Services.AddGggsOutboxRelay<ReportingDbContext>();

builder.Services.AddHostedService<HeadcountSnapshotJob>();

IHost host = builder.Build();
await host.RunAsync();
