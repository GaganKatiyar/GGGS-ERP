using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.Provisioning;
using Gggs.Identity.Application.Users;
using Gggs.Identity.Infrastructure;
using Gggs.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

/*
 * Identity's background half.
 *
 * A separate process from the API on purpose. Event delivery scales on queue depth and the API
 * scales on request traffic, and running the subscriber inside the API would tie the two together:
 * a burst of tenant provisioning would compete with sign-ins for the same threads, and scaling the
 * API to zero at night would stop messages being consumed at all.
 *
 * It shares the API's DbContext and application services, so a handler enforces exactly the same
 * rules an endpoint does. What it does not share is authentication: nobody is signed in here, so
 * writes are attributed to the system user.
 */

const string ServiceName = "identity-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

// No request, so no signed-in user. Writes are attributed to the system identity, which is what
// the audit columns record - and is honest about the fact that no person made this change.
builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("IdentityDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<TenantSeedingService>();

builder.Services.AddGggsMessaging<IdentityDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all, which is why adding a handler and forgetting this line is the one
// mistake that looks wired up and does nothing.
builder.Services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, TenantProvisionedConsumer>();

builder.Services.AddGggsEventSubscriber<IdentityDbContext>();

// This process also drains Identity's own outbox. Both halves of messaging live here so the API
// only ever stages messages inside its own transaction and never talks to the broker.
builder.Services.AddGggsOutboxRelay<IdentityDbContext>();

IHost host = builder.Build();
await host.RunAsync();
