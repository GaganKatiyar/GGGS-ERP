using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.MasterData.Endpoints;
using Gggs.Modules.MasterData.Infrastructure;
using Gggs.Modules.Platform.Contracts;

namespace Gggs.Modules.MasterData;

/// <summary>Tenant-maintained lookup lists (spec §113), and the validation other modules use against them.</summary>
public sealed class MasterDataModule : IModule
{
    public string Name => "master-data";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IMasterDataLookup, MasterDataLookup>();
        services.AddScoped<MasterDataSeeder>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
        => services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedMasterDataOnTenantProvisioned>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapMasterDataEndpoints();
}
