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
using Gggs.Spend.Api.Endpoints;
using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Application.Travel;
using Gggs.Spend.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "spend";

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

builder.Services.AddDbContext<SpendDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SpendDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SpendDbContext>());
builder.Services.AddGggsMessaging<SpendDbContext>(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
builder.Services.AddScoped<IExpenseClaimRepository, ExpenseClaimRepository>();
builder.Services.AddScoped<IExpenseLineRepository, ExpenseLineRepository>();
builder.Services.AddScoped<IEmployeeSnapshotRepository, EmployeeSnapshotRepository>();
builder.Services.AddScoped<ITravelRequestRepository, TravelRequestRepository>();
builder.Services.AddScoped<ITravelAdvanceRepository, TravelAdvanceRepository>();
builder.Services.AddScoped<ITravelSettlementRepository, TravelSettlementRepository>();

builder.Services.AddScoped<ExpenseCategoryService>();
builder.Services.AddScoped<ExpenseClaimService>();
builder.Services.AddScoped<TravelRequestService>();
builder.Services.AddScoped<TravelAdvanceService>();
builder.Services.AddScoped<TravelSettlementService>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<SpendDbContext>("spend-db", tags: ["ready"]);

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

app.MapSpendEndpoints();

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
