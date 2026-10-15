using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Contract.Application;

namespace Gggs.Modules.Contract.Endpoints;

/// <summary>Contracts under <c>/api/v1/contracts</c> (spec §38, §128).</summary>
internal static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/contracts").WithTags("Contracts");

        group.MapGet("/", async ([AsParameters] ContractListQuery query, ContractService contracts, CancellationToken ct) => TypedResults.Ok(await contracts.ListAsync(query, ct)))
            .WithName("ListContracts").RequirePermission(Permissions.Contract.View);
        group.MapGet("/{id:guid}", async (Guid id, ContractService contracts, CancellationToken ct) => TypedResults.Ok(await contracts.GetAsync(id, ct)))
            .WithName("GetContract").RequirePermission(Permissions.Contract.View);
        group.MapPost("/", async (ContractRequest body, ContractService contracts, CancellationToken ct) =>
            {
                ContractDetail created = await contracts.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/contracts/{created.Contract.Id}", created);
            })
            .WithName("CreateContract").RequirePermission(Permissions.Contract.Manage);
        group.MapPut("/{id:guid}", async (Guid id, ContractRequest body, ContractService contracts, CancellationToken ct) => TypedResults.Ok(await contracts.UpdateAsync(id, body, ct)))
            .WithName("UpdateContract").RequirePermission(Permissions.Contract.Manage);
        group.MapPost("/{id:guid}/activate", async (Guid id, ContractService contracts, CancellationToken ct) => TypedResults.Ok(await contracts.ActivateAsync(id, ct)))
            .WithName("ActivateContract").RequirePermission(Permissions.Contract.Manage);
        group.MapPost("/{id:guid}/terminate", async (Guid id, TerminateContractBody body, ContractService contracts, CancellationToken ct) =>
            {
                await contracts.TerminateAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("TerminateContract").RequirePermission(Permissions.Contract.Manage);

        return endpoints;
    }
}
