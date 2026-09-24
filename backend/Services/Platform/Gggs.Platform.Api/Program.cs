using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Platform.Api.Endpoints;
using Gggs.Platform.Application.Provisioning;
using Gggs.Platform.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "platform";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// The deployment profile is read first: it decides which infrastructure adapters are registered,
// and nothing below this line knows whether it runs on Azure or a customer's own server.
// ---------------------------------------------------------------------------------------------
GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsAuthorization(builder.Configuration);

// Temporary: permissions come from token claims until the Identity service lands, at which point
// this call is replaced by the Redis-backed provider that honours role revocation.
builder.Services.AddClaimsPermissionProvider();
builder.Services.AddAuthentication().AddJwtBearer();

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
builder.Services.AddGggsMessaging<PlatformDbContext>(builder.Configuration, platform.DeploymentProfile);

// This service owns the registry, so it reads it straight from its own database rather than
// calling itself over HTTP.
builder.Services.AddScoped<ITenantRegistry, PlatformTenantRegistry>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<IPlanRepository, PlanRepository>();
builder.Services.AddScoped<TenantProvisioningService>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<PlatformDbContext>("platform-db", tags: ["ready"]);

WebApplication app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Order matters: correlation first so every later log line carries it, then authentication, then
// tenant resolution (which needs the validated token), then authorization.
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

// Probes answer without a token: a load balancer has none. Status only, never component detail.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapTenantEndpoints();
app.MapTenantSettingEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Deny by default: refuse to start if any endpoint is reachable without a declared permission.
EndpointSecurityValidator.ThrowIfAnyUnprotected(
    app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
    app.Services.GetRequiredService<ILogger<Program>>());

await app.RunAsync();

/// <summary>Exposed so the integration tests can host this API in-process.</summary>
public partial class Program;
