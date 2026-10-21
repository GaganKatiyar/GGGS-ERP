using System.Text;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Insight.Application;
using Gggs.Modules.Insight.Contracts;
using Hangfire;

namespace Gggs.Modules.Insight;

/// <summary>Analytics and dashboards, global search, the HR calendar, imports and integrations (spec: insight and reach).</summary>
public sealed class InsightModule : IModule
{
    public string Name => "insight";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IAiTool, GetMetricsTool>();
        services.AddScoped<IAiTool, SearchDirectoryTool>();
        services.AddScoped<IAiTool, UpcomingCalendarTool>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<SearchService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<DigestService>();
        services.AddScoped<ImportService>();
        services.AddScoped<IntegrationService>();
        services.AddHttpClient(IntegrationHttp.ClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IIntegrationAdapter, WebhookAdapter>();
        services.AddScoped<IIntegrationAdapter, EntraIdAdapter>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<DigestSweep>();
        services.AddScoped<DigestSweepJob>();
        services.AddHostedService<DigestJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder analytics = endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics");
        analytics.MapGet("/", async (AnalyticsService service, CancellationToken ct) => TypedResults.Ok(await service.AreasAsync(ct)))
            .WithName("AnalyticsAreas").RequirePermission(Permissions.Report.View, Permissions.Report.ExecutiveView);
        analytics.MapGet("/subscriptions", async (DigestService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyReportSubscriptions").RequirePermission(Permissions.Report.ScheduleManage);
        analytics.MapPut("/subscriptions", async (SubscriptionBody body, DigestService service, CancellationToken ct) => TypedResults.Ok(await service.SubscribeAsync(body, ct)))
            .WithName("SubscribeToReport").RequirePermission(Permissions.Report.ScheduleManage);
        analytics.MapDelete("/subscriptions/{id:guid}", async (Guid id, DigestService service, CancellationToken ct) => TypedResults.Ok(await service.UnsubscribeAsync(id, ct)))
            .WithName("UnsubscribeFromReport").RequirePermission(Permissions.Report.ScheduleManage);
        analytics.MapGet("/{area}", async (string area, AnalyticsService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(area, ct)))
            .WithName("AnalyticsDashboard").RequirePermission(Permissions.Report.View, Permissions.Report.ExecutiveView);

        endpoints.MapGet("/api/v1/search", async (string? q, SearchService service, CancellationToken ct) => TypedResults.Ok(await service.SearchAsync(q, ct)))
            .WithTags("Search").WithName("GlobalSearch").RequirePermission(Permissions.Self.ProfileView, Permissions.Employee.View);

        endpoints.MapGet("/api/v1/calendar", async (DateOnly from, DateOnly to, string? scope, CalendarService service, CancellationToken ct) => TypedResults.Ok(await service.EntriesAsync(from, to, scope, ct)))
            .WithTags("Calendar").WithName("HrCalendar").RequirePermission(Permissions.Self.ProfileView, Permissions.Employee.View);

        RouteGroupBuilder imports = endpoints.MapGroup("/api/v1/imports").WithTags("Imports");
        imports.MapGet("/", async (ImportService service, CancellationToken ct) => TypedResults.Ok(await service.HandlersAsync(ct)))
            .WithName("ListImports").RequirePermission(Permissions.DataHub.ImportRun);
        imports.MapGet("/history", async (ImportService service, CancellationToken ct) => TypedResults.Ok(await service.HistoryAsync(ct)))
            .WithName("ImportHistory").RequirePermission(Permissions.DataHub.ImportRun);
        imports.MapGet("/{key}/template", async (string key, ImportService service, CancellationToken ct) =>
                TypedResults.File(Encoding.UTF8.GetBytes(await service.TemplateAsync(key, ct)), "text/csv", $"{key}-template.csv"))
            .WithName("ImportTemplate").RequirePermission(Permissions.DataHub.ImportRun);
        imports.MapPost("/{key}/preview", async (string key, ImportBody body, ImportService service, CancellationToken ct) => TypedResults.Ok(await service.RunAsync(key, body, commit: false, ct)))
            .WithName("PreviewImport").RequirePermission(Permissions.DataHub.ImportRun);
        imports.MapPost("/{key}", async (string key, ImportBody body, ImportService service, CancellationToken ct) => TypedResults.Ok(await service.RunAsync(key, body, commit: true, ct)))
            .WithName("RunImport").RequirePermission(Permissions.DataHub.ImportRun);

        RouteGroupBuilder integrations = endpoints.MapGroup("/api/v1/integrations").WithTags("Integrations");
        integrations.MapGet("/adapters", (IntegrationService service) => TypedResults.Ok(service.Adapters()))
            .WithName("IntegrationAdapters").RequirePermission(Permissions.DataHub.IntegrationView);
        integrations.MapGet("/connections", async (IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.ConnectionsAsync(ct)))
            .WithName("IntegrationConnections").RequirePermission(Permissions.DataHub.IntegrationView);
        integrations.MapPost("/connections", async (ConnectionBody body, IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(null, body, ct)))
            .WithName("CreateIntegrationConnection").RequirePermission(Permissions.DataHub.IntegrationManage);
        integrations.MapPut("/connections/{id:guid}", async (Guid id, ConnectionBody body, IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(id, body, ct)))
            .WithName("UpdateIntegrationConnection").RequirePermission(Permissions.DataHub.IntegrationManage);
        integrations.MapPost("/connections/{id:guid}/test", async (Guid id, IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.TestAsync(id, ct)))
            .WithName("TestIntegrationConnection").RequirePermission(Permissions.DataHub.IntegrationManage);
        integrations.MapPost("/connections/{id:guid}/run", async (Guid id, RunBody body, IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.RunAsync(id, body, ct)))
            .WithName("RunIntegration").RequirePermission(Permissions.DataHub.IntegrationManage);
        integrations.MapGet("/runs", async (Guid? connectionId, IntegrationService service, CancellationToken ct) => TypedResults.Ok(await service.RunsAsync(connectionId, ct)))
            .WithName("IntegrationRuns").RequirePermission(Permissions.DataHub.IntegrationView);
    }
}

/// <summary>Fans the daily digest pass out to every tenant.</summary>
[PlatformJob("Report digests: fans out one digest run per active tenant.")]
internal sealed class DigestSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "insight-report-digests";

    public Task RunAsync() => fanOut.FanOutAsync<DigestSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class DigestSweepJob(DigestService digests, ILogger<DigestSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int sent = await digests.SendDueAsync(CancellationToken.None);
        if (sent > 0)
        {
            logger.LogInformation("Report digests for tenant {TenantId}: {Sent} sent.", tenantId, sent);
        }
    }
}

internal sealed class DigestJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<DigestSweep>(DigestSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 6 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
