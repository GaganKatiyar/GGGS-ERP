using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Endpoints;

public sealed record CreateRoleBody(string Name, string? Description);

public sealed record GrantPermissionBody(string PermissionCode, DataScope Scope);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<PermissionAssignmentResponse> Permissions);

public sealed record PermissionAssignmentResponse(string PermissionCode, string Scope);

/// <summary>A permission as the role editor offers it.</summary>
public sealed record PermissionCatalogItem(
    string Code,
    string Module,
    string Resource,
    string Action,
    bool IsSensitive);

/// <summary>
/// Roles, and the catalog of permissions they can be built from.
/// <para>
/// The catalog is served rather than hard-coded in the UI, so a role editor always offers exactly
/// what the platform actually checks. A screen listing a permission no endpoint enforces would let
/// an administrator grant access that does nothing.
/// </para>
/// </summary>
public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/roles").WithTags("Roles");

        group.MapGet("/", ListAsync).WithName("ListRoles").RequirePermission(Permissions.Admin.RoleView);
        group.MapPost("/", CreateAsync).WithName("CreateRole").RequirePermission(Permissions.Admin.RoleManage);
        group.MapPost("/{roleId:guid}/permissions", GrantAsync).WithName("GrantPermission").RequirePermission(Permissions.Admin.RoleManage);
        group.MapDelete("/{roleId:guid}/permissions/{permissionCode}", RevokeAsync).WithName("RevokePermission").RequirePermission(Permissions.Admin.RoleManage);

        endpoints.MapGet("/api/v1/permissions", ListPermissionsAsync)
            .WithName("ListPermissions")
            .WithTags("Roles")
            .RequirePermission(Permissions.Admin.RoleView);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<RoleResponse>>> ListAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        List<Role> roles = await database.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<RoleResponse>>([.. roles.Select(ToResponse)]);
    }

    private static async Task<Created<RoleResponse>> CreateAsync(
        CreateRoleBody body,
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Role role = Role.Create(body.Name, body.Description);
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/roles/{role.Id}", ToResponse(role));
    }

    private static async Task<Ok<RoleResponse>> GrantAsync(
        Guid roleId,
        GrantPermissionBody body,
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Role role = await roles.GetAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        // The domain refuses an unknown or platform-only code, so a typo here fails rather than
        // creating a role that looks like access and grants nothing.
        role.Grant(body.PermissionCode, body.Scope);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(role));
    }

    private static async Task<Ok<RoleResponse>> RevokeAsync(
        Guid roleId,
        string permissionCode,
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        Role role = await roles.GetAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        role.Revoke(permissionCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(role));
    }

    /// <summary>
    /// The permissions a tenant role may hold. Platform-only codes are filtered out, so they never
    /// appear in a customer's role editor at all.
    /// </summary>
    private static Ok<IReadOnlyList<PermissionCatalogItem>> ListPermissionsAsync()
        => TypedResults.Ok<IReadOnlyList<PermissionCatalogItem>>(
        [
            .. PermissionCatalog.All
                .Where(permission => !permission.IsPlatformOnly)
                .OrderBy(permission => permission.Module)
                .ThenBy(permission => permission.Resource)
                .ThenBy(permission => permission.Action)
                .Select(permission => new PermissionCatalogItem(
                    permission.Code,
                    permission.Module,
                    permission.Resource,
                    permission.Action,
                    permission.IsSensitive))
        ]);

    private static RoleResponse ToResponse(Role role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.IsSystem,
        [.. role.Permissions.Select(permission =>
            new PermissionAssignmentResponse(permission.PermissionCode, permission.Scope.ToString()))]);
}
