using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Sample.Api.Endpoints;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "sample";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Composition. Every service host wires the same building blocks in the same order, so tenancy,
// authorization, telemetry and error handling behave identically across the platform.
//
// The deployment profile is read first, because it decides which infrastructure adapters are
// registered below. Nothing after this line knows whether it is running on Azure or on a
// customer's own server.
// ---------------------------------------------------------------------------------------------
GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsFileStorage(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsAuthorization(builder.Configuration);
builder.Services.AddAuthentication().AddJwtBearer();

builder.Services.AddOptions<SampleTenantOptions>()
    .Bind(builder.Configuration.GetSection(SampleTenantOptions.SectionName));
builder.Services.AddSingleton<ITenantRegistry, ConfiguredTenantRegistry>();
builder.Services.AddClaimsPermissionProvider();

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<SampleDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SampleDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SampleDbContext>());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

// Readiness includes the database: an instance that cannot reach it should leave the rotation
// rather than serve errors.
builder.Services.AddHealthChecks().AddDbContextCheck<SampleDbContext>("sample-db", tags: ["ready"]);

WebApplication app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Pipeline. Order matters: correlation first so every later log line carries it, then
// authentication, then tenant resolution (which needs the validated token), then authorization.
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

// Probes must answer without a token - a load balancer has none - so they opt out of the
// deny-by-default fallback explicitly. They report status only, never component detail.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();
app.MapSampleEndpoints();

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
