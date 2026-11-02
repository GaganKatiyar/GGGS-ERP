using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Identity.Application.Authorization;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>
/// The access screens: who holds which role, and what each person may do and why. Read-only;
/// roles are changed on their own routes (RoleEndpoints, UserEndpoints).
/// </summary>
public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/access/users", async (AccessQueries access, CancellationToken ct) => TypedResults.Ok(await access.UsersAsync(ct)))
            .WithTags("Access")
            .WithName("ListUserAccess")
            .RequirePermission(Permissions.Admin.UserView);

        endpoints.MapGet("/api/v1/users/{userId:guid}/access", async (Guid userId, AccessQueries access, CancellationToken ct) => TypedResults.Ok(await access.UserAsync(userId, ct)))
            .WithTags("Access")
            .WithName("GetUserAccess")
            .RequirePermission(Permissions.Admin.UserView);

        return endpoints;
    }
}
