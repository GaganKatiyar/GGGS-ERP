using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.CoreHr.Application.Assets;
using Gggs.CoreHr.Domain.Assets;
using Gggs.CoreHr.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Api.Endpoints;

public sealed record AssetCategoryResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateAssetCategoryBody(string Code, string Name);

public sealed record AssetResponse(
    Guid Id, string AssetTag, Guid CategoryId, string Make, string Model, string? SerialNumber, AssetStatus Status);

public sealed record CreateAssetBody(string AssetTag, Guid CategoryId, string Make, string Model, string? SerialNumber);

public sealed record AssetAssignmentResponse(Guid Id, Guid AssetId, Guid EmployeeId, DateTime AssignedDate, DateTime? ReturnedDate);

public sealed record AssignAssetBody(Guid EmployeeId);

/// <summary>
/// The asset register: categories, the assets themselves, and assigning/returning them to
/// employees - architecture.md §5.1's Asset module, kept inside Core HR rather than its own
/// service. Admin management through <see cref="Permissions.Asset.Manage"/>; an employee seeing
/// their own assigned assets through <see cref="Permissions.Self.AssetView"/> instead.
/// </summary>
public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapCategoryEndpoints(endpoints);
        MapAssetRegisterEndpoints(endpoints);
        MapAssignmentEndpoints(endpoints);

        return endpoints;
    }

    private static void MapCategoryEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/assets/categories").WithTags("Assets");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<AssetCategoryResponse> items = await db.AssetCategories.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => ToResponse(x))
                .ToListAsync(ct);

            return TypedResults.Ok<IReadOnlyList<AssetCategoryResponse>>(items);
        }).WithName("ListAssetCategories").RequirePermission(Permissions.Asset.View);

        group.MapPost("/", async (CreateAssetCategoryBody body, AssetCategoryService service, CancellationToken ct) =>
        {
            AssetCategory entity = await service.CreateAsync(body.Code, body.Name, ct);
            return TypedResults.Created($"/api/v1/assets/categories/{entity.Id}", ToResponse(entity));
        }).WithName("CreateAssetCategory").RequirePermission(Permissions.Asset.CategoryManage);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, AssetCategoryService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)))
        ).WithName("DeactivateAssetCategory").RequirePermission(Permissions.Asset.CategoryManage);

        group.MapPost("/{id:guid}/reactivate", async (Guid id, AssetCategoryService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)))
        ).WithName("ReactivateAssetCategory").RequirePermission(Permissions.Asset.CategoryManage);
    }

    private static void MapAssetRegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/assets").WithTags("Assets");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<AssetResponse> items = await db.Assets.AsNoTracking()
                .OrderBy(x => x.AssetTag)
                .Select(x => ToResponse(x))
                .ToListAsync(ct);

            return TypedResults.Ok<IReadOnlyList<AssetResponse>>(items);
        }).WithName("ListAssets").RequirePermission(Permissions.Asset.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            Asset? entity = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetAsset").RequirePermission(Permissions.Asset.View);

        group.MapPost("/", async (CreateAssetBody body, AssetService service, CancellationToken ct) =>
        {
            Asset entity = await service.CreateAsync(body.AssetTag, body.CategoryId, body.Make, body.Model, body.SerialNumber, ct);
            return TypedResults.Created($"/api/v1/assets/{entity.Id}", ToResponse(entity));
        }).WithName("CreateAsset").RequirePermission(Permissions.Asset.Manage);

        group.MapPost("/{id:guid}/send-for-repair", async (Guid id, AssetService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.SendForRepairAsync(id, ct)))
        ).WithName("SendAssetForRepair").RequirePermission(Permissions.Asset.Manage);

        group.MapPost("/{id:guid}/return-from-repair", async (Guid id, AssetService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ReturnFromRepairAsync(id, ct)))
        ).WithName("ReturnAssetFromRepair").RequirePermission(Permissions.Asset.Manage);

        group.MapPost("/{id:guid}/retire", async (Guid id, AssetService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RetireAsync(id, ct)))
        ).WithName("RetireAsset").RequirePermission(Permissions.Asset.Manage);
    }

    private static void MapAssignmentEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/assets/assignments/me", async (ICurrentUser currentUser, CoreHrDbContext db, CancellationToken ct) =>
        {
            if (currentUser.EmployeeId is not { } employeeId)
            {
                return TypedResults.Ok<IReadOnlyList<AssetAssignmentResponse>>([]);
            }

            List<AssetAssignmentResponse> items = await db.AssetAssignments.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.AssignedDate)
                .Select(x => ToResponse(x))
                .ToListAsync(ct);

            return TypedResults.Ok<IReadOnlyList<AssetAssignmentResponse>>(items);
        }).WithName("ListMyAssetAssignments").WithTags("Assets").RequirePermission(Permissions.Self.AssetView);

        endpoints.MapGet("/api/v1/assets/employees/{employeeId:guid}/assignments", async (
            Guid employeeId, CoreHrDbContext db, CancellationToken ct) =>
        {
            List<AssetAssignmentResponse> items = await db.AssetAssignments.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.AssignedDate)
                .Select(x => ToResponse(x))
                .ToListAsync(ct);

            return TypedResults.Ok<IReadOnlyList<AssetAssignmentResponse>>(items);
        }).WithName("ListEmployeeAssetAssignments").WithTags("Assets").RequirePermission(Permissions.Asset.View);

        endpoints.MapPost("/api/v1/assets/{id:guid}/assign", async (
            Guid id, AssignAssetBody body, AssetAssignmentService service, CancellationToken ct) =>
        {
            AssetAssignment entity = await service.AssignAsync(id, body.EmployeeId, ct);
            return TypedResults.Created($"/api/v1/assets/assignments/{entity.Id}", ToResponse(entity));
        }).WithName("AssignAsset").WithTags("Assets").RequirePermission(Permissions.Asset.Manage);

        endpoints.MapPost("/api/v1/assets/assignments/{id:guid}/return", async (
            Guid id, AssetAssignmentService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ReturnAsync(id, ct)))
        ).WithName("ReturnAsset").WithTags("Assets").RequirePermission(Permissions.Asset.Manage);
    }

    private static AssetCategoryResponse ToResponse(AssetCategory entity) => new(entity.Id, entity.Code, entity.Name, entity.IsActive);

    private static AssetResponse ToResponse(Asset entity) => new(
        entity.Id, entity.AssetTag, entity.CategoryId, entity.Make, entity.Model, entity.SerialNumber, entity.Status);

    private static AssetAssignmentResponse ToResponse(AssetAssignment entity) => new(
        entity.Id, entity.AssetId, entity.EmployeeId, entity.AssignedDate, entity.ReturnedDate);
}
