using Gggs.Api.Browser;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Composition;
using Gggs.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

/*
 * PeopleOS - the one web host (ADR-12).
 *
 * The browser's only origin and every API client's only address. The SPA is served from here, the
 * session cookie belongs to here, and every module's endpoints run in this process against the one
 * database. There is no gateway and no service-to-service call: a module needing another's data
 * calls its contract interface in-process.
 *
 * Background work - delivering outbox messages, scheduled jobs - runs in Gggs.Worker, so request
 * handling never competes with it.
 */

const string ServiceName = "api";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The deployment profile is read first: it decides which infrastructure adapters are registered,
// and nothing below knows whether it runs on Azure or a customer's own server.
GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsSensitiveData();
builder.Services.AddGggsFileStorage(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsAuthorization(builder.Configuration);

string? redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis))
{
    // Sessions then live in this process only: lost on restart and invisible to other replicas.
    // Fine for one developer instance or a single-node on-premise install; the startup log says so.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddGggsAppDbContext(builder.Configuration, ModuleCatalog.ModelAssemblies);
builder.Services.AddGggsMessaging<AppDbContext>(builder.Configuration);

var moduleContext = new ModuleContext(builder.Configuration, builder.Environment);
foreach (IModule module in ModuleCatalog.All)
{
    module.AddServices(builder.Services, moduleContext);
    module.AddApiServices(builder.Services, moduleContext);
}

builder.Services.AddOptions<SecurityHeaderOptions>()
    .Bind(builder.Configuration.GetSection(SecurityHeaderOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddGggsRateLimiting(builder.Configuration);
builder.Services.AddGggsJson();
builder.Services.AddGggsProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("app-db", tags: ["ready"]);

WebApplication app = builder.Build();

if (string.IsNullOrWhiteSpace(redis))
{
    app.Logger.LogWarning(
        "No Redis connection is configured, so browser sessions are held in memory: they will not " +
        "survive a restart and will not be shared between replicas. Configure ConnectionStrings:Redis " +
        "before running more than one instance.");
}

// Order matters: correlation first so every later log line carries it; security headers on every
// response including errors and static files; the browser guard before anything reads the request;
// then authentication, tenant resolution (which needs the principal), then authorization.
app.UseExceptionHandler();

// An error status with no body - an unknown route, a wrong method, a bare 401 - still gets the
// standard error envelope, so a client never needs a second way to read failures.
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<BrowserRequestGuard>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

// After authentication, so the assistant limit can count per user rather than per address.
app.UseRateLimiter();

// Probes answer without a credential: a load balancer has none. Status only, never component detail.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

foreach (IModule module in ModuleCatalog.All)
{
    module.MapEndpoints(app);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The SPA, from this same origin - which is what makes the cookie work without CORS and without a
// token in the browser. Absent during development, when the Angular dev server serves it and
// proxies /api and /bff here instead.
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Deep links are client-side routes: /employees/123 must return the application, not a 404.
    // Ordered last, so a mistyped API path still fails as an API path rather than returning HTML.
    app.MapFallbackToFile("index.html")
        .AllowAnonymous()
        .Add(endpoint => ((RouteEndpointBuilder)endpoint).Order = int.MaxValue);
}

// Deny by default: refuse to start if any endpoint is reachable without a declared permission.
EndpointSecurityValidator.ThrowIfAnyUnprotected(
    app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
    app.Services.GetRequiredService<ILogger<Program>>());

app.Logger.LogInformation(
    "PeopleOS API starting in the {Profile} profile with {Count} modules.",
    platform.DeploymentProfile,
    ModuleCatalog.All.Count);

await app.RunAsync();

/// <summary>Exposed so the integration tests can host the API in-process.</summary>
public partial class Program;
