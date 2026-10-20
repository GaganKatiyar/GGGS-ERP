using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Performance.Application;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Performance.Domain;
using Hangfire;

namespace Gggs.Modules.Performance;

/// <summary>Goals, performance reviews and improvement plans (spec: goals, performance).</summary>
public sealed class PerformanceModule : IModule
{
    public string Name => "performance";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IAiTool, MyGoalsTool>();
        services.AddScoped<IMetricSource, PerformanceMetrics>();
        services.AddScoped<ICalendarSource, ReviewCalendar>();
        services.AddScoped<GoalService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<PipService>();
        services.AddScoped<IPerformanceRatings, PerformanceRatings>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ReviewReminderSweep>();
        services.AddScoped<ReviewReminderJob>();
        services.AddHostedService<PerformanceJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Goals.
        RouteGroupBuilder goals = endpoints.MapGroup("/api/v1/goals").WithTags("Goals");
        string[] goalAccess = [Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage, Permissions.Performance.ReviewView];
        goals.MapGet("/", async ([AsParameters] GoalQuery query, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.GoalsAsync(query, ct)))
            .WithName("ListGoals").RequirePermission(goalAccess);
        goals.MapGet("/{id:guid}", async (Guid id, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.GoalAsync(id, ct)))
            .WithName("GetGoal").RequirePermission(goalAccess);
        goals.MapPost("/", async (GoalBody body, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.CreateAsync(body, ct)))
            .WithName("CreateGoal").RequirePermission(Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage);
        goals.MapPut("/{id:guid}", async (Guid id, GoalDetails body, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, body, ct)))
            .WithName("UpdateGoal").RequirePermission(Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage);
        goals.MapPut("/{id:guid}/key-results", async (Guid id, IReadOnlyList<KeyResultBody> body, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.SaveKeyResultsAsync(id, body, ct)))
            .WithName("SaveKeyResults").RequirePermission(Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage);
        goals.MapPost("/{id:guid}/check-ins", async (Guid id, CheckInBody body, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.CheckInAsync(id, body, ct)))
            .WithName("CheckInGoal").RequirePermission(Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage);
        goals.MapPost("/{id:guid}/{action:regex(^(activate|complete|cancel|reopen)$)}", async (Guid id, string action, GoalService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, action, ct)))
            .WithName("ChangeGoal").RequirePermission(Permissions.Performance.GoalManageOwn, Permissions.Performance.GoalManage);

        // Review cycles, reviews, calibration.
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/performance").WithTags("Performance");
        group.MapGet("/cycles", async (ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CyclesAsync(ct)))
            .WithName("ListReviewCycles").RequirePermission(Permissions.Performance.CycleView);
        group.MapGet("/cycles/{id:guid}", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CycleAsync(id, ct)))
            .WithName("GetReviewCycle").RequirePermission(Permissions.Performance.CycleView);
        group.MapPost("/cycles", async (CycleDetails body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCycleAsync(null, body, ct)))
            .WithName("CreateReviewCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPut("/cycles/{id:guid}", async (Guid id, CycleDetails body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCycleAsync(id, body, ct)))
            .WithName("UpdateReviewCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPost("/cycles/{id:guid}/launch", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.LaunchAsync(id, ct)))
            .WithName("LaunchReviewCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPost("/cycles/{id:guid}/{action:regex(^(calibrate|close)$)}", async (Guid id, string action, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CycleActionAsync(id, action, ct)))
            .WithName("ChangeReviewCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPost("/cycles/{id:guid}/release", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CycleActionAsync(id, "release", ct)))
            .WithName("ReleaseReviewCycle").RequirePermission(Permissions.Performance.CalibrationManage);
        group.MapGet("/cycles/{id:guid}/calibration", async (Guid id, Guid? departmentId, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CalibrationAsync(id, departmentId, ct)))
            .WithName("GetCalibration").RequirePermission(Permissions.Performance.CalibrationManage);
        group.MapGet("/cycles/{id:guid}/reviews", async (Guid id, [AsParameters] ReviewQuery query, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.ReviewsAsync(id, query, ct)))
            .WithName("ListCycleReviews").RequirePermission(Permissions.Performance.ReviewView, Permissions.Performance.ReviewManageAsManager);

        string[] reviewAccess = [Permissions.Performance.ReviewManageOwn, Permissions.Performance.ReviewView, Permissions.Performance.ReviewManageAsManager];
        group.MapGet("/reviews/{id:guid}", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.ReviewAsync(id, ct)))
            .WithName("GetReview").RequirePermission(reviewAccess);
        group.MapPut("/reviews/{id:guid}/self", async (Guid id, ReviewSaveBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.SaveSelfAsync(id, body, ct)))
            .WithName("SaveSelfReview").RequirePermission(Permissions.Performance.ReviewManageOwn);
        group.MapPut("/reviews/{id:guid}/manager", async (Guid id, ReviewSaveBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.SaveManagerAsync(id, body, ct)))
            .WithName("SaveManagerReview").RequirePermission(Permissions.Performance.ReviewManageAsManager);
        group.MapPost("/reviews/{id:guid}/return", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.ReturnAsync(id, ct)))
            .WithName("ReturnReview").RequirePermission(Permissions.Performance.CalibrationManage);
        group.MapPost("/reviews/{id:guid}/calibrate", async (Guid id, CalibrateBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.CalibrateAsync(id, body, ct)))
            .WithName("CalibrateReview").RequirePermission(Permissions.Performance.CalibrationManage);
        group.MapPost("/reviews/{id:guid}/acknowledge", async (Guid id, CommentBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.AcknowledgeAsync(id, body.Comment, ct)))
            .WithName("AcknowledgeReview").RequirePermission(Permissions.Performance.ReviewManageOwn);
        group.MapPost("/reviews/{id:guid}/feedback", async (Guid id, FeedbackAskBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.AskFeedbackAsync(id, body, ct)))
            .WithName("AskFeedback").RequirePermission(Permissions.Self.FeedbackGive, Permissions.Performance.ReviewManageAsManager);

        // Improvement plans.
        group.MapGet("/pips", async (PipStatus? status, PipService service, CancellationToken ct) => TypedResults.Ok(await service.PlansAsync(status, ct)))
            .WithName("ListImprovementPlans").RequirePermission(Permissions.Performance.PipView, Permissions.Performance.PipManage);
        group.MapGet("/pips/{id:guid}", async (Guid id, PipService service, CancellationToken ct) => TypedResults.Ok(await service.PlanAsync(id, ct)))
            .WithName("GetImprovementPlan").RequirePermission(Permissions.Performance.PipView, Permissions.Performance.PipManage);
        group.MapPost("/pips", async (PipBody body, PipService service, CancellationToken ct) => TypedResults.Ok(await service.CreateAsync(body, ct)))
            .WithName("CreateImprovementPlan").RequirePermission(Permissions.Performance.PipManage);
        group.MapPut("/pips/{id:guid}", async (Guid id, PipDetails body, PipService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, body, ct)))
            .WithName("UpdateImprovementPlan").RequirePermission(Permissions.Performance.PipManage);
        group.MapPost("/pips/{id:guid}/start", async (Guid id, PipService service, CancellationToken ct) => TypedResults.Ok(await service.StartAsync(id, ct)))
            .WithName("StartImprovementPlan").RequirePermission(Permissions.Performance.PipManage);
        group.MapPost("/pips/{id:guid}/notes", async (Guid id, PipNoteBody body, PipService service, CancellationToken ct) => TypedResults.Ok(await service.NoteAsync(id, body.Text, ct)))
            .WithName("AddImprovementPlanNote").RequirePermission(Permissions.Performance.PipManage);
        group.MapPost("/pips/{id:guid}/extend", async (Guid id, PipExtendBody body, PipService service, CancellationToken ct) => TypedResults.Ok(await service.ExtendAsync(id, body.EndDate, ct)))
            .WithName("ExtendImprovementPlan").RequirePermission(Permissions.Performance.PipManage);
        group.MapPost("/pips/{id:guid}/close", async (Guid id, PipCloseBody body, PipService service, CancellationToken ct) => TypedResults.Ok(await service.CloseAsync(id, body, ct)))
            .WithName("CloseImprovementPlan").RequirePermission(Permissions.Performance.PipManage);
        group.MapPost("/pips/{id:guid}/cancel", async (Guid id, PipService service, CancellationToken ct) => TypedResults.Ok(await service.CancelAsync(id, ct)))
            .WithName("CancelImprovementPlan").RequirePermission(Permissions.Performance.PipManage);

        // Self-service.
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Performance");
        me.MapGet("/goals", async (GoalService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyGoals").RequirePermission(Permissions.Performance.GoalManageOwn);
        me.MapGet("/reviews", async (ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(asManager: false, ct)))
            .WithName("MyReviews").RequirePermission(Permissions.Performance.ReviewManageOwn);
        me.MapGet("/reviews/team", async (ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(asManager: true, ct)))
            .WithName("MyTeamReviews").RequirePermission(Permissions.Performance.ReviewManageAsManager);
        me.MapGet("/pips", async (PipService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyImprovementPlans").RequirePermission(Permissions.Performance.ReviewManageOwn);
        me.MapGet("/feedback", async (ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.MyFeedbackAsync(ct)))
            .WithName("MyFeedbackRequests").RequirePermission(Permissions.Self.FeedbackGive);
        me.MapPut("/feedback/{id:guid}", async (Guid id, FeedbackGiveBody body, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.GiveFeedbackAsync(id, body, decline: false, ct)))
            .WithName("GiveFeedback").RequirePermission(Permissions.Self.FeedbackGive);
        me.MapPost("/feedback/{id:guid}/decline", async (Guid id, ReviewService service, CancellationToken ct) => TypedResults.Ok(await service.GiveFeedbackAsync(id, null, decline: true, ct)))
            .WithName("DeclineFeedback").RequirePermission(Permissions.Self.FeedbackGive);
    }
}

/// <summary>Fans the daily review reminder out to every tenant.</summary>
internal sealed class ReviewReminderSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "performance-reminders";

    public Task RunAsync() => fanOut.FanOutAsync<ReviewReminderJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class ReviewReminderJob(ReviewService reviews, ILogger<ReviewReminderJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int sent = await reviews.RemindAsync(CancellationToken.None);
        if (sent > 0)
        {
            logger.LogInformation("Review reminders for tenant {TenantId}: {Count} sent.", tenantId, sent);
        }
    }
}

internal sealed class PerformanceJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<ReviewReminderSweep>(ReviewReminderSweep.RecurringJobName, sweep => sweep.RunAsync(), "15 3 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
