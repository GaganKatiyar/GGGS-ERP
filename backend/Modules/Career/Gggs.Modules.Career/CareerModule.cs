using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Career.Application;
using Gggs.Modules.Career.Domain;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Career;

/// <summary>Career paths, promotions and succession (spec: career and succession).</summary>
public sealed class CareerModule : IModule
{
    public string Name => "career";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<CareerService>();
        services.AddWorkflowSubject(CareerService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApprovePromotionOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectPromotionOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnPromotionOnSendBack>();
        services.AddScoped<PromotionSweep>();
        services.AddScoped<PromotionJob>();
        services.AddHostedService<CareerJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/career").WithTags("Career");

        group.MapGet("/paths", async (CareerService service, CancellationToken ct) => TypedResults.Ok(await service.PathsAsync(ct)))
            .WithName("ListCareerPaths").RequirePermission(Permissions.Career.PathView, Permissions.Self.CareerView);
        group.MapPost("/paths", async (PathBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.SavePathAsync(null, body, ct)))
            .WithName("CreateCareerPath").RequirePermission(Permissions.Career.PathManage);
        group.MapPut("/paths/{id:guid}", async (Guid id, PathBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.SavePathAsync(id, body, ct)))
            .WithName("UpdateCareerPath").RequirePermission(Permissions.Career.PathManage);

        group.MapGet("/promotions", async (PromotionStatus? status, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.PromotionsAsync(status, ct)))
            .WithName("ListPromotions").RequirePermission(Permissions.Career.PromotionView, Permissions.Career.PromotionManage);
        group.MapGet("/promotions/{id:guid}", async (Guid id, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.PromotionAsync(id, ct)))
            .WithName("GetPromotion").RequirePermission(Permissions.Career.PromotionView, Permissions.Career.PromotionManage, Permissions.Workflow.TaskAct);
        group.MapPost("/promotions", async (PromotionBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.NominateAsync(body, ct)))
            .WithName("NominatePromotion").RequirePermission(Permissions.Career.PromotionManage);
        group.MapPut("/promotions/{id:guid}", async (Guid id, PromotionDetails body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.UpdatePromotionAsync(id, body, ct)))
            .WithName("UpdatePromotion").RequirePermission(Permissions.Career.PromotionManage);
        group.MapPost("/promotions/{id:guid}/{action:regex(^(submit|withdraw)$)}", async (Guid id, string action, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.PromotionActionAsync(id, action, ct)))
            .WithName("ChangePromotion").RequirePermission(Permissions.Career.PromotionManage);

        group.MapGet("/succession", async (CareerService service, CancellationToken ct) => TypedResults.Ok(await service.SuccessionAsync(ct)))
            .WithName("ListSuccessionPlans").RequirePermission(Permissions.Career.SuccessionView, Permissions.Career.SuccessionManage);
        group.MapPost("/succession", async (SuccessionBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.SaveSuccessionAsync(null, body, ct)))
            .WithName("CreateSuccessionPlan").RequirePermission(Permissions.Career.SuccessionManage);
        group.MapPut("/succession/{id:guid}", async (Guid id, SuccessionBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.SaveSuccessionAsync(id, body, ct)))
            .WithName("UpdateSuccessionPlan").RequirePermission(Permissions.Career.SuccessionManage);

        group.MapGet("/talent-review", async (Guid? departmentId, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.NineBoxAsync(departmentId, ct)))
            .WithName("TalentReview").RequirePermission(Permissions.Career.TalentReviewManage);
        group.MapPost("/talent-review/assessments", async (AssessmentBody body, CareerService service, CancellationToken ct) => TypedResults.Ok(await service.AssessAsync(body, ct)))
            .WithName("AssessTalent").RequirePermission(Permissions.Career.TalentReviewManage);

        endpoints.MapGroup("/api/v1/me").WithTags("Career")
            .MapGet("/career", async (CareerService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyCareer").RequirePermission(Permissions.Self.CareerView);
    }
}

internal sealed class ApprovePromotionOnApproval(CareerService career) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == CareerService.SubjectType ? career.DecideAsync(integrationEvent.SubjectId, approved: true, cancellationToken) : Task.CompletedTask;
}

internal sealed class RejectPromotionOnRejection(CareerService career) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == CareerService.SubjectType ? career.DecideAsync(integrationEvent.SubjectId, approved: false, cancellationToken) : Task.CompletedTask;
}

internal sealed class ReturnPromotionOnSendBack(CareerService career) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == CareerService.SubjectType ? career.DecideAsync(integrationEvent.SubjectId, approved: null, cancellationToken) : Task.CompletedTask;
}

/// <summary>Fans the daily promotion pass (approved promotions whose date has come) out to every tenant.</summary>
internal sealed class PromotionSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "career-promotions";

    public Task RunAsync() => fanOut.FanOutAsync<PromotionJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class PromotionJob(CareerService career, ILogger<PromotionJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int applied = await career.ApplyDueAsync(CancellationToken.None);
        if (applied > 0)
        {
            logger.LogInformation("Promotions for tenant {TenantId}: {Count} applied.", tenantId, applied);
        }
    }
}

internal sealed class CareerJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<PromotionSweep>(PromotionSweep.RecurringJobName, sweep => sweep.RunAsync(), "5 0 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
