using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Compliance.Application;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Compliance.Endpoints;
using Gggs.Modules.Platform.Contracts;

namespace Gggs.Modules.Compliance;

/// <summary>The compliance centre (spec §22, §107).</summary>
public sealed class ComplianceModule : IModule
{
    public string Name => "compliance";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<RuleService>();
        services.AddScoped<ComplianceCalendar>();
        services.AddScoped<ComplianceSeeder>();
        services.AddScoped<IStatutoryRuleBook, StatutoryRuleBook>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ComplianceSweep>();
        services.AddScoped<ComplianceSweepJob>();
        services.AddHostedService<ComplianceJobRegistration>();
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedComplianceOnTenantProvisioned>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapComplianceEndpoints();
}
