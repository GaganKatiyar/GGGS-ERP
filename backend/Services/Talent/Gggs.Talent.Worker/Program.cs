using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Contracts;
using Gggs.Talent.Application.Snapshots;
using Gggs.Talent.Infrastructure;
using Microsoft.EntityFrameworkCore;

/*
 * Talent's background half.
 *
 * One subscription so far: Core HR's EmployeeAssignmentChangedIntegrationEvent, keeping
 * ref.EmployeeSnapshot current for the hiring-manager display name shown against a requisition
 * (architecture.md §5.2) - EmployeeSnapshotConsumer itself has existed since Talent's first slice,
 * but nothing ever subscribed it until this project. Also relays Talent's own outbox
 * (OfferAcceptedIntegrationEvent, staged by OfferService.AcceptAsync in the same transaction as the
 * acceptance it describes - without this project, that event would sit in the table forever).
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "talent-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<TalentDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TalentDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TalentDbContext>());

builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();

builder.Services.AddGggsMessaging<TalentDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, EmployeeSnapshotConsumer>();

builder.Services.AddGggsEventSubscriber<TalentDbContext>();

// Drains Talent's own outbox (OfferAcceptedIntegrationEvent), so the API never talks to the broker
// directly.
builder.Services.AddGggsOutboxRelay<TalentDbContext>();

IHost host = builder.Build();
await host.RunAsync();
