using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Infrastructure;
using Gggs.Talent.Contracts;
using Microsoft.EntityFrameworkCore;

/*
 * Core HR's background half.
 *
 * One responsibility so far: consuming OfferAcceptedIntegrationEvent (Talent's recruitment pipeline
 * reaching its happy-path end) to auto-hire the Core HR employee - see OfferAcceptedConsumer's own
 * remarks. Also relays Core HR's own outbox (EmployeeAssignmentChangedIntegrationEvent, staged by
 * EmployeeService.HireAsync/TransferAsync/... in the same transaction as the write it describes -
 * without this project, those events would sit in the table forever).
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "corehr-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<CoreHrDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CoreHrDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CoreHrDbContext>());

builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<EmployeeService>();

builder.Services.AddGggsMessaging<CoreHrDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<OfferAcceptedIntegrationEvent, OfferAcceptedConsumer>();

builder.Services.AddGggsEventSubscriber<CoreHrDbContext>();

// Drains Core HR's own outbox (EmployeeAssignmentChangedIntegrationEvent, EmployeeExitedIntegrationEvent),
// so the API never talks to the broker directly.
builder.Services.AddGggsOutboxRelay<CoreHrDbContext>();

IHost host = builder.Build();
await host.RunAsync();
