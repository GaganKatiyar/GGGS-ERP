using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Platform.Application.Audit;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Application.Usage;
using Gggs.Modules.Platform.Endpoints;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.Modules.Platform;

/// <summary>
/// The HR application's view of the platform: who its organisations are (read from the platform
/// console's database, NaBPlatform), each organisation's settings and audit log, and the handshake
/// that carries out the console's requests.
/// <para>
/// Provides the one <see cref="ITenantRegistry"/> the application uses, cached for a minute.
/// </para>
/// </summary>
public sealed class PlatformModule : IModule
{
    public string Name => "platform";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Read when the context is created, not now: a host's configuration can still change between
        // registration and start (the integration tests point it at their own database).
        services.AddDbContext<PlatformCatalogDbContext>((provider, options) => options.UseSqlServer(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(PlatformCatalogDbContext.ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{PlatformCatalogDbContext.ConnectionStringName} is required: the platform console's database (NaBPlatform), where organisations live.")));

        services.AddScoped<ITenantRegistry, PlatformTenantRegistry>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<TenantProvisioningService>();
        services.AddScoped<IActiveTenantProvider, ActiveTenantProvider>();
        services.AddScoped<PlatformUsageRecorder>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        // The handshake with the platform console: its requests every minute, usage hourly, job health
        // every five minutes (see docs/nabplatform-contract.md).
        services.AddScoped<PlatformUsageSweep>();
        services.AddScoped<PlatformUsageJob>();
        services.AddScoped<PlatformSyncJob>();
        services.AddScoped<JobHealthReporter>();
        services.AddHostedService<PlatformUsageJobRegistration>();

        // Every module's audit interceptor stages AuditEntry into the shared outbox; this is the
        // one consumer, writing them into platform.AuditLog.
        services.AddGggsEventConsumer<AuditEntryIntegrationEvent, AuditEntryConsumer>();
    }

    /// <summary>
    /// The organisation's own endpoints only. Organisations, plans and subscriptions are managed in the
    /// platform console (repository GGGS-ERP-PLATFORM).
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTenantSettingEndpoints();
        endpoints.MapAuditEndpoints();
        endpoints.MapAboutEndpoints();
    }
}
