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
using Gggs.Payroll.Api.Endpoints;
using Gggs.Payroll.Application.Attendance;
using Gggs.Payroll.Application.Payroll;
using Gggs.Payroll.Application.Snapshots;
using Gggs.Payroll.Infrastructure;
using Gggs.Payroll.Infrastructure.Attendance;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "payroll";

// QuestPDF requires an explicit licence acknowledgement at startup - Community, free below its
// revenue threshold (architecture.md §15, Directory.Packages.props's own remarks on this package).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

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

// Attendance for run proration comes from the Time service directly - see
// IAttendanceDaysClient's own remarks on why this is a synchronous call rather than a local
// snapshot table fed by events, the usual cross-service pattern.
builder.Services.AddHttpClient<IAttendanceDaysClient, HttpAttendanceDaysClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gggs:Services:Time"] ?? "http://time");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<PayrollDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PayrollDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PayrollDbContext>());
builder.Services.AddGggsMessaging<PayrollDbContext>(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ISalaryComponentRepository, SalaryComponentRepository>();
builder.Services.AddScoped<ISalaryStructureRepository, SalaryStructureRepository>();
builder.Services.AddScoped<ISalaryStructureComponentRepository, SalaryStructureComponentRepository>();
builder.Services.AddScoped<IEmployeeSalaryRepository, EmployeeSalaryRepository>();
builder.Services.AddScoped<IEmployeeSalaryComponentRepository, EmployeeSalaryComponentRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();
builder.Services.AddScoped<IPayGroupRepository, PayGroupRepository>();
builder.Services.AddScoped<IPayPeriodRepository, PayPeriodRepository>();
builder.Services.AddScoped<IPayrollRunRepository, PayrollRunRepository>();
builder.Services.AddScoped<IPayrollRunEmployeeRepository, PayrollRunEmployeeRepository>();
builder.Services.AddScoped<IPayrollRunLineRepository, PayrollRunLineRepository>();
builder.Services.AddScoped<IPayslipRepository, PayslipRepository>();

builder.Services.AddScoped<SalaryComponentService>();
builder.Services.AddScoped<SalaryStructureService>();
builder.Services.AddScoped<EmployeeSalaryService>();
builder.Services.AddScoped<PayGroupService>();
builder.Services.AddScoped<PayPeriodService>();
builder.Services.AddScoped<PayrollRunService>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<PayrollDbContext>("payroll-db", tags: ["ready"]);

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

app.MapPayrollEndpoints();

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
