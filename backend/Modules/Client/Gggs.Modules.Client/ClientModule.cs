using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Client.Application;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Client.Endpoints;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Client;

/// <summary>Clients and their contacts (spec §37).</summary>
public sealed class ClientModule : IModule
{
    public string Name => "clients";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ISearchSource, ClientSearch>();
        services.AddScoped<ClientService>();
        services.AddScoped<IClientDirectory, ClientDirectory>();
        services.AddScoped<IDocumentOwnerAccess, ClientDocumentAccess>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapClientEndpoints();
}
