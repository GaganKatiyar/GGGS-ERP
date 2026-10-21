using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Onboarding.Application;
using Gggs.Modules.Platform.Contracts;
using Hangfire;

namespace Gggs.Modules.Onboarding;

/// <summary>Pre-boarding and onboarding (spec §13).</summary>
public sealed class OnboardingModule : IModule
{
    public string Name => "onboarding";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IMetricSource, OnboardingMetrics>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<OnboardingSeeder>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedOnboardingOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmployeeHiredIntegrationEvent, StartOnboardingOnHire>();
        services.AddScoped<OnboardingDailySweep>();
        services.AddScoped<OnboardingDailyJob>();
        services.AddHostedService<OnboardingJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/onboarding").WithTags("Onboarding");

        group.MapGet("/dashboard", async (OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.DashboardAsync(ct)))
            .WithName("OnboardingDashboard").RequirePermission(Permissions.Onboarding.CaseView);

        group.MapGet("/templates", async (OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.TemplatesAsync(ct)))
            .WithName("ListOnboardingTemplates").RequirePermission(Permissions.Onboarding.TemplateView, Permissions.Onboarding.CaseManage);
        group.MapPost("/templates", async (TemplateBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.SaveTemplateAsync(null, body, ct)))
            .WithName("CreateOnboardingTemplate").RequirePermission(Permissions.Onboarding.TemplateManage);
        group.MapPut("/templates/{id:guid}", async (Guid id, TemplateBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.SaveTemplateAsync(id, body, ct)))
            .WithName("UpdateOnboardingTemplate").RequirePermission(Permissions.Onboarding.TemplateManage);

        group.MapGet("/owners", async (OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.OwnersAsync(ct)))
            .WithName("GetOnboardingOwners").RequirePermission(Permissions.Onboarding.TemplateView, Permissions.Onboarding.CaseManage);
        group.MapPut("/owners", async (OwnersBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.SaveOwnersAsync(body, ct)))
            .WithName("SaveOnboardingOwners").RequirePermission(Permissions.Onboarding.TemplateManage);

        group.MapGet("/cases", async ([AsParameters] CaseQuery query, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.CasesAsync(query, ct)))
            .WithName("ListOnboardingCases").RequirePermission(Permissions.Onboarding.CaseView);
        group.MapGet("/cases/{id:guid}", async (Guid id, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.CaseAsync(id, ct)))
            .WithName("GetOnboardingCase").RequirePermission(Permissions.Onboarding.CaseView);
        group.MapPost("/cases", async (StartBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.StartAsync(body, ct)))
            .WithName("StartOnboarding").RequirePermission(Permissions.Onboarding.CaseManage);
        group.MapPut("/cases/{id:guid}/buddy", async (Guid id, BuddyBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.AssignBuddyAsync(id, body.BuddyEmployeeId, ct)))
            .WithName("AssignOnboardingBuddy").RequirePermission(Permissions.Onboarding.CaseManage);
        group.MapPost("/cases/{id:guid}/cancel", async (Guid id, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.CancelAsync(id, ct)))
            .WithName("CancelOnboarding").RequirePermission(Permissions.Onboarding.CaseManage);
        group.MapPut("/tasks/{id:guid}", async (Guid id, TaskChangeBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.ChangeTaskAsync(id, body, ct)))
            .WithName("ChangeOnboardingTask").RequirePermission(Permissions.Onboarding.CaseManage);

        // Closing a task: its assignee (anyone), IT/admin task completers, or case managers - checked per task.
        group.MapPost("/tasks/{id:guid}/finish", async (Guid id, FinishBody body, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.FinishTaskAsync(id, body, ct)))
            .WithName("FinishOnboardingTask").RequirePermission(Permissions.Self.OnboardingView, Permissions.Onboarding.TaskComplete, Permissions.Onboarding.CaseManage);
        group.MapPost("/tasks/{id:guid}/reopen", async (Guid id, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.ReopenTaskAsync(id, ct)))
            .WithName("ReopenOnboardingTask").RequirePermission(Permissions.Self.OnboardingView, Permissions.Onboarding.TaskComplete, Permissions.Onboarding.CaseManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/onboarding").WithTags("Onboarding");
        me.MapGet("/", async (OnboardingService onboarding, CancellationToken ct) => await onboarding.MyCaseAsync(ct) is { } view ? Results.Ok(view) : Results.NoContent())
            .WithName("MyOnboarding").RequirePermission(Permissions.Self.OnboardingView);
        me.MapGet("/tasks", async (bool? includeDone, OnboardingService onboarding, CancellationToken ct) => TypedResults.Ok(await onboarding.MyTasksAsync(includeDone == true, ct)))
            .WithName("MyOnboardingTasks").RequirePermission(Permissions.Self.OnboardingView);
    }
}

internal sealed class SeedOnboardingOnTenantProvisioned(OnboardingSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal sealed class StartOnboardingOnHire(OnboardingService onboarding) : IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return onboarding.StartForHireAsync(integrationEvent.EmployeeId, integrationEvent.JoiningDate, integrationEvent.ManagerEmployeeId, cancellationToken);
    }
}

/// <summary>Fans the daily onboarding pass out to every tenant.</summary>
[PlatformJob("Onboarding daily sweep: fans out one advance and reminder pass per active tenant.")]
internal sealed class OnboardingDailySweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "onboarding-daily";

    public Task RunAsync() => fanOut.FanOutAsync<OnboardingDailyJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class OnboardingDailyJob(OnboardingService onboarding, ILogger<OnboardingDailyJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int reminded = await onboarding.DailyAsync(CancellationToken.None);
        if (reminded > 0)
        {
            logger.LogInformation("Onboarding for tenant {TenantId}: {Count} overdue task reminder(s) sent.", tenantId, reminded);
        }
    }
}

internal sealed class OnboardingJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Early morning (UTC), before the working day in India.
        recurringJobs.AddOrUpdate<OnboardingDailySweep>(OnboardingDailySweep.RecurringJobName, sweep => sweep.RunAsync(), "0 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
