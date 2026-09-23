using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Sample.Api.Domain;
using Gggs.Sample.Api.Endpoints;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Composition. Every service host wires the same building blocks in the same order, so tenancy,
// authorization and error handling behave identically across the platform.
// ---------------------------------------------------------------------------------------------
builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsAuthorization(builder.Configuration);
builder.Services.AddAuthentication().AddJwtBearer();

builder.Services.AddOptions<SampleTenantOptions>()
    .Bind(builder.Configuration.GetSection(SampleTenantOptions.SectionName));
builder.Services.AddSingleton<ITenantRegistry, ConfiguredTenantRegistry>();
builder.Services.AddScoped<IPermissionProvider, ClaimsPermissionProvider>();

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<SampleDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SampleDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SampleDbContext>());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<SampleDbContext>("sample-db");

WebApplication app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Pipeline. Order matters: correlation first so every later log line carries it, then
// authentication, then tenant resolution (which needs the validated token), then authorization.
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapSampleEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Deny by default: refuse to start if any endpoint is reachable without a declared permission.
EndpointSecurityValidator.ThrowIfAnyUnprotected(
    app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
    app.Services.GetRequiredService<ILogger<Program>>());

await app.RunAsync();

/// <summary>Exposed so the integration tests can host this API in-process.</summary>
public partial class Program;
