using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Composition;
using Gggs.Data;

/*
 * PeopleOS - the background host (ADR-12).
 *
 * A separate process from the API on purpose: background work scales on backlog, the API on request
 * traffic, and running both in one process would let a burst of either starve the other.
 *
 * Two jobs live here. The outbox dispatcher delivers every module's integration events to the
 * consumers registered for them, in-process and against the same database. Hangfire runs scheduled
 * and queued work, with the tenant carried from enqueue to execution.
 *
 * It composes the same modules from the same catalog as the API, so a consumer enforces exactly the
 * rules an endpoint does. What it does not share is a signed-in user: writes here are attributed to
 * the system identity, which is what the audit columns record.
 */

const string ServiceName = "worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsSensitiveData();
builder.Services.AddGggsFileStorage(builder.Configuration, platform.DeploymentProfile);

// No request, so no signed-in user. Writes are attributed to the system identity - honest about the
// fact that no person made the change.
builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddGggsAppDbContext(builder.Configuration, ModuleCatalog.ModelAssemblies);
builder.Services.AddGggsMessaging<AppDbContext>(builder.Configuration);

var moduleContext = new ModuleContext(builder.Configuration, builder.Environment);
foreach (IModule module in ModuleCatalog.All)
{
    module.AddServices(builder.Services, moduleContext);
    module.AddBackgroundServices(builder.Services, moduleContext);
}

builder.Services.AddGggsOutboxDispatcher<AppDbContext>();

builder.Services.AddGggsJobs(builder.Configuration, DataExtensions.ConnectionStringName);
builder.Services.AddGggsJobServer();

IHost host = builder.Build();
await host.RunAsync();
