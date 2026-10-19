using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Engagement.Application;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Insight.Contracts;
using Hangfire;

namespace Gggs.Modules.Engagement;

/// <summary>Engagement surveys and eNPS (spec: engagement).</summary>
public sealed class EngagementModule : IModule
{
    public string Name => "engagement";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<EngagementService>();
        services.AddScoped<IMetricSource, EngagementMetrics>();
        services.AddScoped<ICalendarSource, SurveyCalendar>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<SurveySweep>();
        services.AddScoped<SurveySweepJob>();
        services.AddHostedService<SurveyJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/engagement").WithTags("Engagement");

        group.MapGet("/dashboard", async (EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("EngagementDashboard").RequirePermission(Permissions.Engagement.ResultsView);
        group.MapGet("/templates/pulse", () => TypedResults.Ok(EngagementService.PulseTemplate))
            .WithName("PulseSurveyTemplate").RequirePermission(Permissions.Engagement.SurveyManage);
        group.MapGet("/surveys", async (SurveyStatus? status, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.SurveysAsync(status, ct)))
            .WithName("ListSurveys").RequirePermission(Permissions.Engagement.SurveyView);
        group.MapGet("/surveys/{id:guid}", async (Guid id, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.SurveyAsync(id, ct)))
            .WithName("GetSurvey").RequirePermission(Permissions.Engagement.SurveyView);
        group.MapPost("/surveys", async (SurveyBody body, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(null, body, ct)))
            .WithName("CreateSurvey").RequirePermission(Permissions.Engagement.SurveyManage);
        group.MapPut("/surveys/{id:guid}", async (Guid id, SurveyBody body, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(id, body, ct)))
            .WithName("UpdateSurvey").RequirePermission(Permissions.Engagement.SurveyManage);
        group.MapPost("/surveys/{id:guid}/publish", async (Guid id, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.PublishAsync(id, ct)))
            .WithName("PublishSurvey").RequirePermission(Permissions.Engagement.SurveyManage);
        group.MapPost("/surveys/{id:guid}/close", async (Guid id, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.CloseAsync(id, ct)))
            .WithName("CloseSurvey").RequirePermission(Permissions.Engagement.SurveyManage);
        group.MapGet("/surveys/{id:guid}/results", async (Guid id, EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.ResultsAsync(id, ct)))
            .WithName("SurveyResults").RequirePermission(Permissions.Engagement.ResultsView);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/surveys").WithTags("Engagement");
        me.MapGet("/", async (EngagementService service, CancellationToken ct) => TypedResults.Ok(await service.MySurveysAsync(ct)))
            .WithName("MySurveys").RequirePermission(Permissions.Self.SurveyRespond);
        me.MapPost("/{id:guid}/respond", async (Guid id, RespondBody body, EngagementService service, CancellationToken ct) =>
            {
                await service.RespondAsync(id, body?.Answers, ct);
                return TypedResults.NoContent();
            })
            .WithName("RespondToSurvey").RequirePermission(Permissions.Self.SurveyRespond);
    }
}

/// <summary>Fans the daily survey sweep (open, close, remind) out to every tenant.</summary>
internal sealed class SurveySweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "engagement-survey-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<SurveySweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class SurveySweepJob(EngagementService engagement, ILogger<SurveySweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        (int opened, int closed, int reminded) = await engagement.SweepAsync(CancellationToken.None);
        if (opened + closed + reminded > 0)
        {
            logger.LogInformation("Survey sweep for tenant {TenantId}: {Opened} opened, {Closed} closed, {Reminded} reminder(s).", tenantId, opened, closed, reminded);
        }
    }
}

internal sealed class SurveyJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<SurveySweep>(SurveySweep.RecurringJobName, sweep => sweep.RunAsync(), "15 1 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
