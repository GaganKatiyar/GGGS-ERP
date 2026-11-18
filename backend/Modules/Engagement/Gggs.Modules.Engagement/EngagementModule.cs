using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Engagement.Application;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Insight.Contracts;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Gggs.Modules.Engagement;

/// <summary>Engagement surveys and eNPS (spec: engagement).</summary>
public sealed class EngagementModule : IModule
{
    public string Name => "engagement";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<EngagementService>();
        services.AddScoped<FeatureBannerService>();
        services.AddScoped<CelebrationService>();
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

        // Reuses the survey permission: both are HR talking to staff, and existing organisations' saved
        // roles never receive a newly added permission, so a new one would leave them unable to use this.
        RouteGroupBuilder banners = group.MapGroup("/banners");
        banners.MapGet("/", async (FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .WithName("ListFeatureBanners").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapPost("/", async (FeatureBannerContent body, FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(null, body, ct)))
            .WithName("CreateFeatureBanner").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapPut("/{id:guid}", async (Guid id, FeatureBannerContent body, FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(id, body, ct)))
            .WithName("UpdateFeatureBanner").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapPost("/{id:guid}/publish", async (Guid id, FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.SetPublishedAsync(id, true, ct)))
            .WithName("PublishFeatureBanner").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapPost("/{id:guid}/unpublish", async (Guid id, FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.SetPublishedAsync(id, false, ct)))
            .WithName("UnpublishFeatureBanner").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapDelete("/{id:guid}", async (Guid id, FeatureBannerService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteFeatureBanner").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapPut("/{id:guid}/image", async (Guid id, IFormFile file, FeatureBannerService service, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(file);
                return TypedResults.Ok(await service.ReplaceImageAsync(id, new IncomingBannerImage(file.Length, file.OpenReadStream), ct));
            })
            .WithName("ReplaceFeatureBannerImage").RequirePermission(Permissions.Engagement.SurveyManage)
            .DisableAntiforgery(); // Guarded by the X-CSRF header check (BrowserRequestGuard), as every cookie write is.
        banners.MapDelete("/{id:guid}/image", async (Guid id, FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.RemoveImageAsync(id, ct)))
            .WithName("RemoveFeatureBannerImage").RequirePermission(Permissions.Engagement.SurveyManage);
        banners.MapGet("/{id:guid}/image", async (Guid id, Guid? v, HttpContext http, FeatureBannerService service, CancellationToken ct) => Image(http, v, await service.OpenImageAsync(id, ct)))
            .WithName("GetFeatureBannerImage").RequirePermission(Permissions.Engagement.SurveyManage);

        // Birthday and work-anniversary greetings, sent by hand. The survey permission again, for the
        // same reason as the banner: HR talking to staff, and saved roles never gain new permissions.
        RouteGroupBuilder celebrations = group.MapGroup("/celebrations");
        celebrations.MapGet("/", async (CelebrationKind kind, DateOnly? from, DateOnly? to, CelebrationService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(kind, from, to, ct)))
            .WithName("ListCelebrations").RequirePermission(Permissions.Engagement.SurveyManage);
        celebrations.MapPost("/send", async (SendWishesBody body, CelebrationService service, CancellationToken ct) => TypedResults.Ok(await service.SendAsync(body, ct)))
            .WithName("SendCelebrationWishes").RequirePermission(Permissions.Engagement.SurveyManage);

        // Everyone with a home dashboard: the same permission that opens the calendar on it.
        endpoints.MapGet("/api/v1/me/banners", async (FeatureBannerService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithTags("Engagement").WithName("MyFeatureBanners").RequirePermission(Permissions.Self.ProfileView, Permissions.Employee.View);
        endpoints.MapGet("/api/v1/me/banners/{id:guid}/image", async (Guid id, Guid? v, HttpContext http, FeatureBannerService service, CancellationToken ct) => Image(http, v, await service.OpenMyImageAsync(id, ct)))
            .WithTags("Engagement").WithName("MyFeatureBannerImage").RequirePermission(Permissions.Self.ProfileView, Permissions.Employee.View);
    }

    /// <summary>Image tags fetch these with the session cookie. A request naming the current picture may be cached for good, because a new picture gets a new id and so a new URL.</summary>
    private static IResult Image(HttpContext http, Guid? version, BannerImage? image)
    {
        if (image is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = version == image.ImageId ? "private, max-age=31536000, immutable" : "private, no-cache";
        return TypedResults.File(image.Content, image.ContentType, entityTag: new EntityTagHeaderValue($"\"{image.ImageId:N}\""));
    }
}

/// <summary>Fans the daily survey sweep (open, close, remind) out to every tenant.</summary>
[PlatformJob("Survey sweep: fans out one survey open, close and reminder pass per active tenant.")]
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
