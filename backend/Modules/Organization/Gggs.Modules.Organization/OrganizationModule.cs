using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Organization.Endpoints;
using Gggs.Modules.Organization.Infrastructure;

namespace Gggs.Modules.Organization;

/// <summary>
/// Organization structure (spec §10): companies, business units, divisions, departments,
/// locations, cost and profit centers, grades, job levels, jobs and positions.
/// </summary>
public sealed class OrganizationModule : IModule
{
    public string Name => "organization";

    public void AddServices(IServiceCollection services, ModuleContext context)
        => services.AddScoped<IOrgStructure, OrgStructure>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapOrganizationEndpoints();
}
