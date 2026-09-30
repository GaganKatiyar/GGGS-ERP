using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Api.Endpoints;
using Gggs.Talent.Application.Learning;
using Gggs.Talent.Application.Onboarding;
using Gggs.Talent.Application.Performance;
using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Application.Snapshots;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "talent";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsAuthorization(builder.Configuration);

// Bearer tokens only: nothing navigates to this service, so there is no cookie to accept.
builder.Services.AddGggsTokenValidation(builder.Configuration);

// Permissions come from Identity per request rather than from the token, so a role removed a
// minute ago takes effect now instead of whenever the token happens to expire.
builder.Services.AddGggsRemotePermissions(builder.Configuration);

// Tenant lookups go to the Platform service, through the same client every downstream service
// uses - this service holds no copy of the tenant registry.
builder.Services.AddGggsRemoteTenantRegistry(builder.Configuration);

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<TalentDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TalentDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TalentDbContext>());
builder.Services.AddGggsMessaging<TalentDbContext>(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<IJobRequisitionRepository, JobRequisitionRepository>();
builder.Services.AddScoped<IJobPostingRepository, JobPostingRepository>();
builder.Services.AddScoped<ICandidateRepository, CandidateRepository>();
builder.Services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
builder.Services.AddScoped<IOfferRepository, OfferRepository>();
builder.Services.AddScoped<IOnboardingTemplateRepository, OnboardingTemplateRepository>();
builder.Services.AddScoped<IOnboardingCaseRepository, OnboardingCaseRepository>();
builder.Services.AddScoped<IPerformanceCycleRepository, PerformanceCycleRepository>();
builder.Services.AddScoped<IGoalRepository, GoalRepository>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<ICourseRepository, CourseRepository>();
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();

builder.Services.AddScoped<JobRequisitionService>();
builder.Services.AddScoped<JobPostingService>();
builder.Services.AddScoped<JobApplicationService>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddScoped<OnboardingTemplateService>();
builder.Services.AddScoped<OnboardingCaseService>();
builder.Services.AddScoped<PerformanceCycleService>();
builder.Services.AddScoped<GoalService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<CourseService>();
builder.Services.AddScoped<EnrollmentService>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<TalentDbContext>("talent-db", tags: ["ready"]);

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapJobRequisitionEndpoints();
app.MapJobPostingEndpoints();
app.MapJobApplicationEndpoints();
app.MapOfferEndpoints();
app.MapOnboardingTemplateEndpoints();
app.MapOnboardingCaseEndpoints();
app.MapPerformanceCycleEndpoints();
app.MapGoalEndpoints();
app.MapReviewEndpoints();
app.MapCourseEndpoints();
app.MapEnrollmentEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

EndpointSecurityValidator.ThrowIfAnyUnprotected(
    app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
    app.Services.GetRequiredService<ILogger<Program>>());

await app.RunAsync();

/// <summary>Exposed so the integration tests can host this API in-process.</summary>
public partial class Program;
