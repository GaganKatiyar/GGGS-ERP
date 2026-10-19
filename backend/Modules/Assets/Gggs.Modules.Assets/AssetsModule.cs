using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Assets.Application;
using Gggs.Modules.Assets.Contracts;
using Gggs.Modules.Assets.Domain;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Platform.Contracts;

namespace Gggs.Modules.Assets;

/// <summary>The asset register (spec: assets).</summary>
public sealed class AssetsModule : IModule
{
    public string Name => "assets";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IMetricSource, AssetMetrics>();
        services.AddScoped<ISearchSource, AssetSearch>();
        services.AddScoped<IImportHandler, AssetImport>();
        services.AddScoped<AssetService>();
        services.AddScoped<AssetSeeder>();
        services.AddScoped<IAssetLedger, AssetLedger>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
        => services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedAssetsOnTenantProvisioned>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/assets").WithTags("Assets");

        group.MapGet("/dashboard", async (AssetService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("AssetDashboard").RequirePermission(Permissions.Asset.View);
        group.MapGet("/categories", async (AssetService service, CancellationToken ct) => TypedResults.Ok(await service.CategoriesAsync(ct)))
            .WithName("ListAssetCategories").RequirePermission(Permissions.Asset.View, Permissions.Self.AssetView);
        group.MapPost("/categories", async (CategoryBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(null, body, ct)))
            .WithName("CreateAssetCategory").RequirePermission(Permissions.Asset.CategoryManage);
        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(id, body, ct)))
            .WithName("UpdateAssetCategory").RequirePermission(Permissions.Asset.CategoryManage);

        group.MapGet("/", async ([AsParameters] AssetQuery query, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.AssetsAsync(query, ct)))
            .WithName("ListAssets").RequirePermission(Permissions.Asset.View);
        group.MapGet("/{id:guid}", async (Guid id, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.AssetAsync(id, ct)))
            .WithName("GetAsset").RequirePermission(Permissions.Asset.View);
        group.MapPost("/", async (AssetBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.RegisterAsync(body, ct)))
            .WithName("RegisterAsset").RequirePermission(Permissions.Asset.Manage);
        group.MapPut("/{id:guid}", async (Guid id, AssetDetails body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, body, ct)))
            .WithName("UpdateAsset").RequirePermission(Permissions.Asset.Manage);
        group.MapPost("/{id:guid}/assign", async (Guid id, AssignBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.AssignAsync(id, body, ct)))
            .WithName("AssignAsset").RequirePermission(Permissions.Asset.Manage);
        group.MapPost("/{id:guid}/return", async (Guid id, ReturnBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.ReturnAsync(id, body, ct)))
            .WithName("ReturnAsset").RequirePermission(Permissions.Asset.Manage);
        group.MapPost("/{id:guid}/{action:regex(^(repair|repaired|retire|lost|note)$)}", async (Guid id, string action, EventBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.ActAsync(id, action, body, ct)))
            .WithName("ChangeAsset").RequirePermission(Permissions.Asset.Manage);

        group.MapGet("/requests", async (AssetRequestStatus? status, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.RequestsAsync(status, ct)))
            .WithName("ListAssetRequests").RequirePermission(Permissions.Asset.View);
        group.MapPost("/requests/{id:guid}/reject", async (Guid id, RejectBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.CloseRequestAsync(id, cancelled: false, body.Response, ct)))
            .WithName("RejectAssetRequest").RequirePermission(Permissions.Asset.Manage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/assets").WithTags("Assets");
        me.MapGet("/", async (AssetService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyAssets").RequirePermission(Permissions.Self.AssetView);
        me.MapGet("/requests", async (AssetService service, CancellationToken ct) => TypedResults.Ok(await service.MyRequestsAsync(ct)))
            .WithName("MyAssetRequests").RequirePermission(Permissions.Self.AssetView);
        me.MapPost("/requests", async (RaiseBody body, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.RaiseAsync(body, ct)))
            .WithName("RequestAsset").RequirePermission(Permissions.Self.AssetView);
        me.MapPost("/requests/{id:guid}/cancel", async (Guid id, AssetService service, CancellationToken ct) => TypedResults.Ok(await service.CloseRequestAsync(id, cancelled: true, null, ct)))
            .WithName("CancelAssetRequest").RequirePermission(Permissions.Self.AssetView);
    }
}

internal sealed class SeedAssetsOnTenantProvisioned(AssetSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}
