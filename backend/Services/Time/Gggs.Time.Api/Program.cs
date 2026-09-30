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
using Gggs.Time.Api.Endpoints;
using Gggs.Time.Application.Attendance;
using Gggs.Time.Application.Holidays;
using Gggs.Time.Application.Leave;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "time";

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

builder.Services.AddDbContext<TimeDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TimeDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TimeDbContext>());
builder.Services.AddGggsMessaging<TimeDbContext>(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ILeaveTypeRepository, LeaveTypeRepository>();
builder.Services.AddScoped<ILeavePolicyRepository, LeavePolicyRepository>();
builder.Services.AddScoped<ILeaveLedgerRepository, LeaveLedgerRepository>();
builder.Services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
builder.Services.AddScoped<IHolidayRepository, HolidayRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<IShiftRosterRepository, ShiftRosterRepository>();
builder.Services.AddScoped<IAttendancePunchRepository, AttendancePunchRepository>();
builder.Services.AddScoped<IAttendanceDayRepository, AttendanceDayRepository>();
builder.Services.AddScoped<IAttendanceRegularizationRepository, AttendanceRegularizationRepository>();

builder.Services.AddScoped<IWorkCalendar, WorkCalendar>();
builder.Services.AddScoped<LeaveLedgerService>();
builder.Services.AddScoped<LeavePolicyResolver>();
builder.Services.AddScoped<LeaveRequestService>();
builder.Services.AddScoped<LeaveTypeService>();
builder.Services.AddScoped<LeavePolicyService>();
builder.Services.AddScoped<HolidayCalendarService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<ShiftRosterService>();
builder.Services.AddScoped<AttendanceDayProcessor>();
builder.Services.AddScoped<AttendancePunchService>();
builder.Services.AddScoped<AttendanceRegularizationService>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<TimeDbContext>("time-db", tags: ["ready"]);

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

app.MapLeaveEndpoints();
app.MapHolidayEndpoints();
app.MapAttendanceEndpoints();
app.MapReportEndpoints();

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
