using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Identity.Application.ApiKeys;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>API keys under <c>/api/v1/api-keys</c>: machine accounts are users, so user administration manages them.</summary>
internal static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/api-keys").WithTags("API keys");

        group.MapGet("/", async (ApiKeyService keys, CancellationToken ct) => TypedResults.Ok(await keys.ListAsync(ct)))
            .WithName("ListApiKeys").RequirePermission(Permissions.Admin.UserView);

        group.MapPost("/", async (CreateApiKeyRequest body, ApiKeyService keys, CancellationToken ct) =>
            {
                CreatedApiKey created = await keys.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/api-keys/{created.Key.Id}", created);
            })
            .WithName("CreateApiKey").RequirePermission(Permissions.Admin.UserManage);

        group.MapPost("/{id:guid}/revoke", async Task<NoContent> (Guid id, ApiKeyService keys, CancellationToken ct) =>
            {
                await keys.RevokeAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("RevokeApiKey").RequirePermission(Permissions.Admin.UserManage);

        return endpoints;
    }
}
