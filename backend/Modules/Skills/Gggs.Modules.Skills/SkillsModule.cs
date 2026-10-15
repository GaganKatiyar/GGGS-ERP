using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Skills.Application;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Skills.Endpoints;

namespace Gggs.Modules.Skills;

/// <summary>Skills and competency (spec §29).</summary>
public sealed class SkillsModule : IModule
{
    public string Name => "skills";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<SkillService>();
        services.AddScoped<SkillCatalogSeeder>();
        services.AddScoped<ISkillDirectory, SkillDirectory>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
        => services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedSkillCatalogOnTenantProvisioned>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapSkillEndpoints();
}
