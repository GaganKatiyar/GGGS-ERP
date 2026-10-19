using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Learning.Application;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Platform.Contracts;
using Hangfire;

namespace Gggs.Modules.Learning;

/// <summary>Learning (spec: courses, paths, certifications and expiry).</summary>
public sealed class LearningModule : IModule
{
    public string Name => "learning";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<LearningService>();
        services.AddScoped<LearningSeeder>();
        services.AddScoped<ILearningRecords, LearningRecords>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedLearningOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmployeeHiredIntegrationEvent, AssignJoinerLearningOnHire>();
        services.AddScoped<LearningDailySweep>();
        services.AddScoped<LearningDailyJob>();
        services.AddHostedService<LearningJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/learning").WithTags("Learning");

        group.MapGet("/dashboard", async (LearningService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("LearningDashboard").RequirePermission(Permissions.Learning.EnrollmentView);
        group.MapGet("/courses", async (bool? includeInactive, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.CoursesAsync(includeInactive == true, ct)))
            .WithName("ListCourses").RequirePermission(Permissions.Learning.CourseView, Permissions.Self.LearningView);
        group.MapPost("/courses", async (CourseBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCourseAsync(null, body, ct)))
            .WithName("CreateCourse").RequirePermission(Permissions.Learning.CourseManage);
        group.MapPut("/courses/{id:guid}", async (Guid id, CourseBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCourseAsync(id, body, ct)))
            .WithName("UpdateCourse").RequirePermission(Permissions.Learning.CourseManage);
        group.MapGet("/paths", async (LearningService service, CancellationToken ct) => TypedResults.Ok(await service.PathsAsync(ct)))
            .WithName("ListLearningPaths").RequirePermission(Permissions.Learning.CourseView, Permissions.Self.LearningView);
        group.MapPost("/paths", async (PathBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.SavePathAsync(null, body, ct)))
            .WithName("CreateLearningPath").RequirePermission(Permissions.Learning.CourseManage);
        group.MapPut("/paths/{id:guid}", async (Guid id, PathBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.SavePathAsync(id, body, ct)))
            .WithName("UpdateLearningPath").RequirePermission(Permissions.Learning.CourseManage);

        group.MapGet("/enrollments", async ([AsParameters] EnrollmentQuery query, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.EnrollmentsAsync(query, ct)))
            .WithName("ListEnrollments").RequirePermission(Permissions.Learning.EnrollmentView);
        group.MapPost("/enrollments/assign", async (AssignBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.AssignAsync(body, ct)))
            .WithName("AssignLearning").RequirePermission(Permissions.Learning.EnrollmentManage);
        group.MapGet("/certificates", async (bool? expiring, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.CertificatesAsync(expiring == true, ct)))
            .WithName("ListCertificates").RequirePermission(Permissions.Learning.CertificationView);

        string[] act = [Permissions.Learning.EnrollmentManageOwn, Permissions.Learning.EnrollmentManage];
        group.MapPost("/enrollments/{id:guid}/start", async (Guid id, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, "start", null, null, null, ct)))
            .WithName("StartEnrollment").RequirePermission(act);
        group.MapPost("/enrollments/{id:guid}/progress", async (Guid id, ProgressBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, "progress", body, null, null, ct)))
            .WithName("RecordEnrollmentProgress").RequirePermission(act);
        group.MapPost("/enrollments/{id:guid}/complete", async (Guid id, CompleteBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, "complete", null, body, null, ct)))
            .WithName("CompleteEnrollment").RequirePermission(act);
        group.MapPost("/enrollments/{id:guid}/rate", async (Guid id, RateBody body, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, "rate", null, null, body, ct)))
            .WithName("RateEnrollment").RequirePermission(act);
        group.MapPost("/enrollments/{id:guid}/cancel", async (Guid id, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, "cancel", null, null, null, ct)))
            .WithName("CancelEnrollment").RequirePermission(act);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/learning").WithTags("Learning");
        me.MapGet("/", async (LearningService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyLearning").RequirePermission(Permissions.Self.LearningView);
        me.MapGet("/catalog", async (LearningService service, CancellationToken ct) => TypedResults.Ok(await service.CatalogAsync(ct)))
            .WithName("MyLearningCatalog").RequirePermission(Permissions.Self.LearningView);
        me.MapPost("/courses/{courseId:guid}/enroll", async (Guid courseId, LearningService service, CancellationToken ct) => TypedResults.Ok(await service.EnrollSelfAsync(courseId, ct)))
            .WithName("EnrollMyself").RequirePermission(Permissions.Learning.EnrollmentManageOwn);
    }
}

internal sealed class SeedLearningOnTenantProvisioned(LearningSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal sealed class AssignJoinerLearningOnHire(LearningService learning) : IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return learning.AssignJoinerAsync(integrationEvent.EmployeeId, integrationEvent.JoiningDate, cancellationToken);
    }
}

/// <summary>Fans the daily learning pass out to every tenant.</summary>
internal sealed class LearningDailySweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "learning-daily";

    public Task RunAsync() => fanOut.FanOutAsync<LearningDailyJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class LearningDailyJob(LearningService learning, ILogger<LearningDailyJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int sent = await learning.DailyAsync(CancellationToken.None);
        if (sent > 0)
        {
            logger.LogInformation("Learning for tenant {TenantId}: {Count} reminder(s) and renewal(s).", tenantId, sent);
        }
    }
}

internal sealed class LearningJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<LearningDailySweep>(LearningDailySweep.RecurringJobName, sweep => sweep.RunAsync(), "45 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
