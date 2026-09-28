using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.Platform.Application.Audit;
using Gggs.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;

/*
 * Platform's background half.
 *
 * A separate process from the API for the same reason every other service splits this way: event
 * delivery scales on queue depth, not on request traffic, and the API scaling to zero overnight
 * must not also stop messages being consumed.
 *
 * Two responsibilities live here. First, this process drains Platform's own outbox - until this
 * project existed, nothing did, so messages like TenantProvisioned sat in the table forever outside
 * of tests that fake the transport. Second, and the reason this project was added, it is the one
 * subscriber for AuditEntryIntegrationEvent: every service raises that event into its own outbox,
 * and this is where all of them land, in the one database a tenant admin's audit log actually reads
 * from.
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "platform-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<PlatformDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PlatformDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlatformDbContext>());
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

builder.Services.AddGggsMessaging<PlatformDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<AuditEntryIntegrationEvent, AuditEntryConsumer>();

builder.Services.AddGggsEventSubscriber<PlatformDbContext>();

// Drains Platform's own outbox (tenant provisioning announcements and anything raised later), so
// the API only ever stages messages inside its own transaction and never talks to the broker.
builder.Services.AddGggsOutboxRelay<PlatformDbContext>();

IHost host = builder.Build();
await host.RunAsync();
