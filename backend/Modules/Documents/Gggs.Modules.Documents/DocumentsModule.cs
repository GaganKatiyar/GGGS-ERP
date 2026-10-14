using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Documents.Application;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Documents.Endpoints;
using Gggs.Modules.Documents.Infrastructure;
using Gggs.Modules.Platform.Contracts;

namespace Gggs.Modules.Documents;

/// <summary>
/// Documents (spec §107): typed, versioned files kept in file storage, letters generated from
/// templates, and signatures through an e-sign port. File contents never go into the database.
/// </summary>
public sealed class DocumentsModule : IModule
{
    public string Name => "documents";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentCatalogSeeder>();
        services.AddScoped<IDocumentDirectory, DocumentDirectory>();
        services.AddScoped<ISignatureProvider, BuiltInSignatureProvider>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
        => services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedDocumentCatalogOnTenantProvisioned>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapDocumentEndpoints();
}
