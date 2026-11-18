using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Organization.Endpoints;
using Gggs.Modules.Organization.Infrastructure;

namespace Gggs.Modules.Organization;

/// <summary>
/// Organization structure (spec §10): companies, business units, departments and their functions,
/// locations, cost centers, roles, levels and designations.
/// </summary>
public sealed class OrganizationModule : IModule
{
    public string Name => "organization";

    public void AddServices(IServiceCollection services, ModuleContext context)
        => services.AddScoped<IOrgStructure, OrgStructure>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapOrganizationEndpoints();
}
