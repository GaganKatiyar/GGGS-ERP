using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Rewards.Application;
using Gggs.Modules.Rewards.Domain;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Rewards;

/// <summary>Rewards and recognition (spec: rewards).</summary>
public sealed class RewardsModule : IModule
{
    public string Name => "rewards";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<RewardService>();
        services.AddScoped<RewardSeeder>();
        services.AddWorkflowSubject(RewardService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedRewardsOnTenantProvisioned>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveRecognitionOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectRecognitionOnRejection>();
        services.AddGggsEventConsumer<ReviewReleasedIntegrationEvent, RewardOnReviewReleased>();
        services.AddGggsEventConsumer<CourseCompletedIntegrationEvent, RewardOnCourseCompleted>();
        services.AddScoped<RewardSweep>();
        services.AddScoped<RewardSweepJob>();
        services.AddHostedService<RewardJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/rewards").WithTags("Rewards");

        group.MapGet("/dashboard", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("RewardDashboard").RequirePermission(Permissions.Reward.ProgramManage, Permissions.Reward.RecognitionView);
        group.MapGet("/settings", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SettingsAsync(ct)))
            .WithName("GetRewardSettings").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPut("/settings", async (SettingsView body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveSettingsAsync(body, ct)))
            .WithName("SaveRewardSettings").RequirePermission(Permissions.Reward.ProgramManage);

        group.MapGet("/types", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.TypesAsync(ct)))
            .WithName("ListRecognitionTypes").RequirePermission(Permissions.Reward.ProgramManage, Permissions.Self.RecognitionGive);
        group.MapPost("/types", async (TypeBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveTypeAsync(null, body, ct)))
            .WithName("CreateRecognitionType").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPut("/types/{id:guid}", async (Guid id, TypeBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveTypeAsync(id, body, ct)))
            .WithName("UpdateRecognitionType").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapGet("/rules", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.RulesAsync(ct)))
            .WithName("ListRewardRules").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPost("/rules", async (RuleBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveRuleAsync(null, body, ct)))
            .WithName("CreateRewardRule").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPut("/rules/{id:guid}", async (Guid id, RuleBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveRuleAsync(id, body, ct)))
            .WithName("UpdateRewardRule").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapGet("/budgets", async (int? year, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.BudgetsAsync(year, ct)))
            .WithName("ListRewardBudgets").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPut("/budgets", async (BudgetBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveBudgetAsync(body, ct)))
            .WithName("SaveRewardBudget").RequirePermission(Permissions.Reward.ProgramManage);
        group.MapPost("/points/adjust", async (AdjustBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.AdjustAsync(body, ct)))
            .WithName("AdjustPoints").RequirePermission(Permissions.Reward.ProgramManage);

        group.MapGet("/feed", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.FeedAsync(ct)))
            .WithName("RecognitionFeed").RequirePermission(Permissions.Self.RewardView, Permissions.Reward.RecognitionView);
        group.MapGet("/recognitions", async (RecognitionStatus? status, Guid? employeeId, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.RecognitionsAsync(status, employeeId, ct)))
            .WithName("ListRecognitions").RequirePermission(Permissions.Reward.RecognitionView);
        group.MapGet("/employees/{employeeId:guid}/recognitions", async (Guid employeeId, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.ReceivedAsync(employeeId, ct)))
            .WithName("EmployeeRecognitions").RequirePermission(Permissions.Self.RewardView, Permissions.Reward.RecognitionView);
        group.MapPost("/recognitions", async (GiveBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.GiveAsync(body, ct)))
            .WithName("GiveRecognition").RequirePermission(Permissions.Self.RecognitionGive);
        group.MapPost("/recognitions/{id:guid}/cheer", async (Guid id, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.CheerAsync(id, ct)))
            .WithName("CheerRecognition").RequirePermission(Permissions.Self.RewardView);

        group.MapGet("/catalog", async (bool? all, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.CatalogAsync(all == true, ct)))
            .WithName("RewardCatalog").RequirePermission(Permissions.Reward.CatalogView, Permissions.Self.RewardView);
        group.MapPost("/catalog", async (CatalogBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveItemAsync(null, body, ct)))
            .WithName("CreateCatalogItem").RequirePermission(Permissions.Reward.CatalogManage);
        group.MapPut("/catalog/{id:guid}", async (Guid id, CatalogBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.SaveItemAsync(id, body, ct)))
            .WithName("UpdateCatalogItem").RequirePermission(Permissions.Reward.CatalogManage);
        group.MapGet("/redemptions", async (RedemptionStatus? status, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.RedemptionsAsync(status, ct)))
            .WithName("ListRedemptions").RequirePermission(Permissions.Reward.RedemptionManage);
        group.MapPost("/redemptions/{id:guid}/fulfil", async (Guid id, NoteBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.FulfilAsync(id, body?.Note, ct)))
            .WithName("FulfilRedemption").RequirePermission(Permissions.Reward.RedemptionManage);
        group.MapPost("/redemptions/{id:guid}/cancel", async (Guid id, NoteBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.CancelRedemptionAsync(id, body?.Note, own: false, ct)))
            .WithName("CancelRedemption").RequirePermission(Permissions.Reward.RedemptionManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/rewards").WithTags("Rewards");
        me.MapGet("/", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.WalletAsync(ct)))
            .WithName("MyRewardWallet").RequirePermission(Permissions.Self.RewardView);
        me.MapGet("/ledger", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.LedgerAsync(ct)))
            .WithName("MyPointsLedger").RequirePermission(Permissions.Self.RewardView);
        me.MapGet("/redemptions", async (RewardService service, CancellationToken ct) => TypedResults.Ok(await service.MyRedemptionsAsync(ct)))
            .WithName("MyRedemptions").RequirePermission(Permissions.Self.RewardView);
        me.MapPost("/redeem", async (RedeemBody body, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.RedeemAsync(body.ItemId, ct)))
            .WithName("RedeemReward").RequirePermission(Permissions.Self.RewardView);
        me.MapPost("/redemptions/{id:guid}/cancel", async (Guid id, RewardService service, CancellationToken ct) => TypedResults.Ok(await service.CancelRedemptionAsync(id, null, own: true, ct)))
            .WithName("CancelMyRedemption").RequirePermission(Permissions.Self.RewardView);
    }
}

internal sealed class SeedRewardsOnTenantProvisioned(RewardSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal sealed class ApproveRecognitionOnApproval(RewardService rewards) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == RewardService.SubjectType ? rewards.DecideAsync(integrationEvent.SubjectId, approved: true, cancellationToken) : Task.CompletedTask;
}

internal sealed class RejectRecognitionOnRejection(RewardService rewards) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == RewardService.SubjectType ? rewards.DecideAsync(integrationEvent.SubjectId, approved: false, cancellationToken) : Task.CompletedTask;
}

internal sealed class RewardOnReviewReleased(RewardService rewards) : IIntegrationEventConsumer<ReviewReleasedIntegrationEvent>
{
    public Task HandleAsync(ReviewReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return rewards.OnReviewReleasedAsync(integrationEvent.EmployeeId, integrationEvent.ReviewId, integrationEvent.FinalRating, cancellationToken);
    }
}

internal sealed class RewardOnCourseCompleted(RewardService rewards) : IIntegrationEventConsumer<CourseCompletedIntegrationEvent>
{
    public Task HandleAsync(CourseCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return rewards.OnCourseCompletedAsync(integrationEvent.EmployeeId, integrationEvent.EnrollmentId, integrationEvent.CourseTitle, cancellationToken);
    }
}

/// <summary>Fans the daily rewards sweep (anniversaries, expiry, expiry warnings) out to every tenant.</summary>
internal sealed class RewardSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "rewards-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<RewardSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class RewardSweepJob(RewardService rewards, ILogger<RewardSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        (int anniversaries, int expired, int warned) = await rewards.SweepAsync(CancellationToken.None);
        if (anniversaries + expired + warned > 0)
        {
            logger.LogInformation("Rewards sweep for tenant {TenantId}: {Anniversaries} anniversary award(s), {Expired} credit(s) lapsed, {Warned} expiry warning(s).", tenantId, anniversaries, expired, warned);
        }
    }
}

internal sealed class RewardJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<RewardSweep>(RewardSweep.RecurringJobName, sweep => sweep.RunAsync(), "30 0 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
