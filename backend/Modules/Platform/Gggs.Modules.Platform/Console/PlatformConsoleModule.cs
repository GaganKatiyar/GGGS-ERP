using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.Modules.Platform.Console;

/// <summary>
/// The platform console's side of the Platform module: organisations, plans, subscriptions, the
/// dashboard, health and the staff audit trail, under <c>/api/v1/platform</c>.
/// <para>
/// A module of its own so only the console server (Gggs.PlatformApi) lists it: the HR application's
/// API never serves these endpoints, and the HR app's code never ships them.
/// </para>
/// </summary>
public sealed class PlatformConsoleModule : IModule
{
    public string Name => "platform-console";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<PlatformAuditWriter>();
        services.AddScoped<Contracts.IPlatformAuditLog>(provider => provider.GetRequiredService<PlatformAuditWriter>());
        services.AddScoped<OrganisationConsole>();
        services.AddScoped<OrganisationDatabaseConsole>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder platform = endpoints.MapGroup("/api/v1/platform").WithTags("Platform console");

        platform.MapGet("/dashboard", async (OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.DashboardAsync(ct)))
            .WithName("ConsoleDashboard").RequirePermission(Permissions.Platform.TenantView);

        RouteGroupBuilder organisations = platform.MapGroup("/organisations");
        organisations.MapGet("/", async ([FromQuery] TenantStatus? status, [FromQuery] string? search, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.ListAsync(status, search, ct)))
            .WithName("ConsoleOrganisations").RequirePermission(Permissions.Platform.TenantView);
        organisations.MapGet("/{id:guid}", async (Guid id, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.DetailAsync(id, ct)))
            .WithName("ConsoleOrganisation").RequirePermission(Permissions.Platform.TenantView);
        organisations.MapGet("/{id:guid}/activity", async (Guid id, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.ActivityAsync(id, 100, ct)))
            .WithName("ConsoleOrganisationActivity").RequirePermission(Permissions.Platform.TenantView);
        organisations.MapPost("/", async Task<Created<OrganisationDetail>> (CreateOrganisationBody body, OrganisationConsole console, CancellationToken ct) =>
            {
                OrganisationDetail created = await console.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/platform/organisations/{created.Id}", created);
            })
            .WithName("ConsoleCreateOrganisation").RequirePermission(Permissions.Platform.TenantManage);
        organisations.MapPost("/{id:guid}/activate", async (Guid id, OrganisationConsole console, CancellationToken ct) => await Done(console.ActivateAsync(id, ct)))
            .WithName("ConsoleActivateOrganisation").RequirePermission(Permissions.Platform.TenantManage);
        organisations.MapPost("/{id:guid}/suspend", async (Guid id, SuspendBody body, OrganisationConsole console, CancellationToken ct) => await Done(console.SuspendAsync(id, body.Reason, ct)))
            .WithName("ConsoleSuspendOrganisation").RequirePermission(Permissions.Platform.TenantSuspend);
        organisations.MapPost("/{id:guid}/reactivate", async (Guid id, OrganisationConsole console, CancellationToken ct) => await Done(console.ReactivateAsync(id, ct)))
            .WithName("ConsoleReactivateOrganisation").RequirePermission(Permissions.Platform.TenantSuspend);
        organisations.MapPost("/{id:guid}/offboard", async (Guid id, OffboardBody body, OrganisationConsole console, CancellationToken ct) => await Done(console.OffboardAsync(id, body.GraceDays, ct)))
            .WithName("ConsoleOffboardOrganisation").RequirePermission(Permissions.Platform.TenantManage);
        organisations.MapPut("/{id:guid}/subscription", async (Guid id, UpdateSubscriptionBody body, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.UpdateSubscriptionAsync(id, body, ct)))
            .WithName("ConsoleUpdateSubscription").RequirePermission(Permissions.Platform.SubscriptionManage);
        organisations.MapPut("/{id:guid}/modules", async (Guid id, UpdateModulesBody body, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.UpdateModulesAsync(id, body, ct)))
            .WithName("ConsoleUpdateModules").RequirePermission(Permissions.Platform.FeatureManage);

        // Where an organisation's data is stored. Staff administrators only: the password opens it.
        RouteGroupBuilder database = organisations.MapGroup("/{id:guid}/database");
        database.MapGet("/", async (Guid id, OrganisationDatabaseConsole console, CancellationToken ct) => TypedResults.Ok(await console.GetAsync(id, ct)))
            .WithName("ConsoleOrganisationDatabase").RequirePermission(Permissions.Platform.DatabaseManage);
        database.MapPut("/", async (Guid id, SaveDatabaseBody body, OrganisationDatabaseConsole console, CancellationToken ct) => TypedResults.Ok(await console.SaveAsync(id, body, ct)))
            .WithName("ConsoleSaveOrganisationDatabase").RequirePermission(Permissions.Platform.DatabaseManage);
        database.MapDelete("/", async (Guid id, OrganisationDatabaseConsole console, CancellationToken ct) => await Done(console.RemoveAsync(id, ct)))
            .WithName("ConsoleRemoveOrganisationDatabase").RequirePermission(Permissions.Platform.DatabaseManage);
        database.MapPost("/test", async (Guid id, SaveDatabaseBody body, OrganisationDatabaseConsole console, CancellationToken ct) => TypedResults.Ok(await console.TestAsync(id, body, ct)))
            .WithName("ConsoleTestOrganisationDatabase").RequirePermission(Permissions.Platform.DatabaseManage);
        database.MapPost("/reveal", async (Guid id, RevealDatabasePasswordBody body, OrganisationDatabaseConsole console, CancellationToken ct) => TypedResults.Ok(await console.RevealAsync(id, body.Reason, ct)))
            .WithName("ConsoleRevealOrganisationDatabasePassword").RequirePermission(Permissions.Platform.DatabaseManage);

        platform.MapGet("/plans", async (OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.PlansAsync(ct)))
            .WithName("ConsolePlans").RequirePermission(Permissions.Platform.TenantView);
        platform.MapGet("/audit", async ([FromQuery] int? take, OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.ActivityAsync(null, take ?? 200, ct)))
            .WithName("ConsoleAudit").RequirePermission(Permissions.Platform.TenantView);
        platform.MapGet("/health", async (OrganisationConsole console, CancellationToken ct) => TypedResults.Ok(await console.HealthAsync(ct)))
            .WithName("ConsoleHealth").RequirePermission(Permissions.Platform.HealthView);
        platform.MapPost("/health/jobs/{jobId}/retry", async (string jobId, OrganisationConsole console, CancellationToken ct) =>
            {
                console.RetryJob(jobId);
                await console.SaveAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleRetryJob").RequirePermission(Permissions.Platform.HealthView);
    }

    private static async Task<NoContent> Done(Task work)
    {
        await work;
        return TypedResults.NoContent();
    }
}
