using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Client.Application;

namespace Gggs.Modules.Client.Endpoints;

/// <summary>Clients and contacts under <c>/api/v1/clients</c> (spec §37, §128).</summary>
internal static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/clients").WithTags("Clients");

        group.MapGet("/", async ([AsParameters] ClientListQuery query, ClientService clients, CancellationToken ct) => TypedResults.Ok(await clients.ListAsync(query, ct)))
            .WithName("ListClients").RequirePermission(Permissions.Client.View);
        group.MapGet("/contacts", async ([AsParameters] ContactListQuery query, ClientService clients, CancellationToken ct) => TypedResults.Ok(await clients.ContactsAsync(query, ct)))
            .WithName("ListClientContacts").RequirePermission(Permissions.Client.View);
        group.MapGet("/{id:guid}", async (Guid id, ClientService clients, CancellationToken ct) => TypedResults.Ok(await clients.GetAsync(id, ct)))
            .WithName("GetClient").RequirePermission(Permissions.Client.View);
        group.MapPost("/", async (ClientRequest body, ClientService clients, CancellationToken ct) =>
            {
                ClientDetail created = await clients.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/clients/{created.Client.Id}", created);
            })
            .WithName("CreateClient").RequirePermission(Permissions.Client.Manage);
        group.MapPut("/{id:guid}", async (Guid id, ClientRequest body, ClientService clients, CancellationToken ct) => TypedResults.Ok(await clients.UpdateAsync(id, body, ct)))
            .WithName("UpdateClient").RequirePermission(Permissions.Client.Manage);
        group.MapPost("/{id:guid}/status", async (Guid id, ClientStatusBody body, ClientService clients, CancellationToken ct) =>
            {
                await clients.SetStatusAsync(id, body.Status, ct);
                return TypedResults.NoContent();
            })
            .WithName("SetClientStatus").RequirePermission(Permissions.Client.Manage);

        group.MapPost("/{id:guid}/contacts", async (Guid id, ContactBody body, ClientService clients, CancellationToken ct) => TypedResults.Ok(await clients.AddContactAsync(id, body, ct)))
            .WithName("AddClientContact").RequirePermission(Permissions.Client.ContactManage);
        group.MapPut("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ContactBody body, ClientService clients, CancellationToken ct) =>
                TypedResults.Ok(await clients.UpdateContactAsync(id, contactId, body, ct)))
            .WithName("UpdateClientContact").RequirePermission(Permissions.Client.ContactManage);
        group.MapDelete("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ClientService clients, CancellationToken ct) =>
            {
                await clients.RemoveContactAsync(id, contactId, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveClientContact").RequirePermission(Permissions.Client.ContactManage);

        return endpoints;
    }
}
