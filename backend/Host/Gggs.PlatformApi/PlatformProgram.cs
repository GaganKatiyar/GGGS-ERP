using Gggs.Api.Browser;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Jobs;
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

namespace Gggs.PlatformApi;

/// <summary>
/// Nuts and Bolts platform console server - GGGS's own staff managing organisations, subscriptions,
/// platform staff and the platform's health.
/// <para>
/// Deployed and addressed apart from the HR application, and serving only the console's endpoints
/// (<see cref="ModuleCatalog.ConsoleModules"/>). It shares one thing with the HR application: the
/// database, so an organisation created here is a tenant there. To write that database the way the
/// HR application does - same model, same events, same seeding - it registers every module's
/// services; it just never maps their endpoints. Integration events it raises (a new organisation, an
/// administrator's invitation) are delivered by the shared Gggs.Worker, as every other event is.
/// </para>
/// <para>
/// A class with a <c>Main</c> rather than top-level statements so its entry point is not called
/// <c>Program</c>: the integration tests host this and the HR API side by side.
/// </para>
/// </summary>
public sealed class PlatformProgram
{
    private const string ServiceName = "platform-api";

    private PlatformProgram()
    {
    }

    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
            builder.Services.AddDistributedMemoryCache();
        }

        builder.Services.AddGggsAppDbContext(builder.Configuration, ModuleCatalog.ModelAssemblies);
        builder.Services.AddGggsMessaging<AppDbContext>(builder.Configuration);

        // Read-only use of the job store: the Health screen lists recurring and failed jobs and can
        // re-queue one. This host never runs a job; the worker does.
        builder.Services.AddGggsJobs(builder.Configuration, DataExtensions.ConnectionStringName);

        builder.Services.AddSingleton(ModuleCatalog.All);

        var moduleContext = new ModuleContext(builder.Configuration, builder.Environment);
        foreach (IModule module in ModuleCatalog.All.Concat(ModuleCatalog.ConsoleModules))
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
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("app-db", tags: ["ready"]);

        WebApplication app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<BrowserRequestGuard>();
        app.UseAuthentication();
        app.UseGggsTenantContext();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .AllowAnonymous();

        foreach (IModule module in ModuleCatalog.ConsoleModules)
        {
            module.MapEndpoints(app);
        }

        // The console's built files, when deployed together; during development the Angular dev
        // server serves them and proxies /api and /bff here.
        if (Directory.Exists(app.Environment.WebRootPath))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html")
                .AllowAnonymous()
                .Add(endpoint => ((RouteEndpointBuilder)endpoint).Order = int.MaxValue);
        }

        EndpointSecurityValidator.ThrowIfAnyUnprotected(
            app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
            app.Services.GetRequiredService<ILogger<PlatformProgram>>());

        app.Logger.LogInformation(
            "Nuts and Bolts platform console server starting in the {Profile} profile.",
            platform.DeploymentProfile);

        await app.RunAsync();
    }
}
