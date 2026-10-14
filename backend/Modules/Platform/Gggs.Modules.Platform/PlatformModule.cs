using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Platform.Application.Audit;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Endpoints;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.Modules.Platform;

/// <summary>
/// Tenants, plans, subscriptions, tenant settings and the audit log.
/// <para>
/// Owns the tenant registry every request resolves through, so this module also provides the one
/// <see cref="ITenantRegistry"/> the application uses - read straight from the database, cached.
/// </para>
/// </summary>
public sealed class PlatformModule : IModule
{
    public string Name => "platform";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ITenantRegistry, PlatformTenantRegistry>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<TenantProvisioningService>();
        services.AddScoped<IActiveTenantProvider, ActiveTenantProvider>();
        services.AddScoped<PlanCatalogSynchronizer>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddHostedService<PlanCatalogSeeder>();

        // Every module's audit interceptor stages AuditEntry into the shared outbox; this is the
        // one consumer, writing them into platform.AuditLog.
        services.AddGggsEventConsumer<AuditEntryIntegrationEvent, AuditEntryConsumer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTenantEndpoints();
        endpoints.MapTenantSettingEndpoints();
        endpoints.MapAuditEndpoints();
    }
}
