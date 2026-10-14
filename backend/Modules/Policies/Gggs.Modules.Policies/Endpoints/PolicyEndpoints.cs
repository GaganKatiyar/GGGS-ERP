using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Policies.Application;

namespace Gggs.Modules.Policies.Endpoints;

/// <summary>Policies (spec §110), and each employee's own policies under <c>/api/v1/me</c>.</summary>
internal static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/policies").WithTags("Policies");

        group.MapGet("/", async (bool? includeRetired, PolicyService policies, CancellationToken ct) => TypedResults.Ok(await policies.ListAsync(includeRetired == true, ct)))
            .WithName("ListPolicies").RequirePermission(Permissions.Policy.View);
        group.MapGet("/{id:guid}", async (Guid id, PolicyService policies, CancellationToken ct) => TypedResults.Ok(await policies.GetAsync(id, ct)))
            .WithName("GetPolicy").RequirePermission(Permissions.Policy.View);
        group.MapPost("/", async (PolicyRequest body, PolicyService policies, CancellationToken ct) =>
            {
                PolicyDetail created = await policies.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/policies/{created.Policy.Id}", created);
            })
            .WithName("CreatePolicy").RequirePermission(Permissions.Policy.Manage);
        group.MapPut("/{id:guid}", async (Guid id, PolicyRequest body, PolicyService policies, CancellationToken ct) => TypedResults.Ok(await policies.UpdateAsync(id, body, ct)))
            .WithName("UpdatePolicy").RequirePermission(Permissions.Policy.Manage);
        group.MapPost("/{id:guid}/retire", async (Guid id, PolicyService policies, CancellationToken ct) =>
            {
                await policies.SetRetiredAsync(id, retired: true, ct);
                return TypedResults.NoContent();
            })
            .WithName("RetirePolicy").RequirePermission(Permissions.Policy.Manage);
        group.MapPost("/{id:guid}/reinstate", async (Guid id, PolicyService policies, CancellationToken ct) =>
            {
                await policies.SetRetiredAsync(id, retired: false, ct);
                return TypedResults.NoContent();
            })
            .WithName("ReinstatePolicy").RequirePermission(Permissions.Policy.Manage);

        group.MapPost("/{id:guid}/versions", async (Guid id, PolicyVersionRequest body, PolicyService policies, CancellationToken ct) =>
                TypedResults.Ok(await policies.AddDraftAsync(id, body, ct)))
            .WithName("AddPolicyDraft").RequirePermission(Permissions.Policy.Manage);
        group.MapPut("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, PolicyVersionRequest body, PolicyService policies, CancellationToken ct) =>
                TypedResults.Ok(await policies.EditDraftAsync(id, versionId, body, ct)))
            .WithName("EditPolicyDraft").RequirePermission(Permissions.Policy.Manage);
        group.MapPost("/{id:guid}/versions/{versionId:guid}/publish", async (Guid id, Guid versionId, PolicyService policies, CancellationToken ct) =>
                TypedResults.Ok(await policies.PublishAsync(id, versionId, ct)))
            .WithName("PublishPolicyVersion").RequirePermission(Permissions.Policy.Manage);

        group.MapGet("/{id:guid}/acknowledgements", async (Guid id, Guid? versionId, PolicyService policies, CancellationToken ct) =>
                TypedResults.Ok(await policies.AcknowledgementsAsync(id, versionId, ct)))
            .WithName("PolicyAcknowledgements").RequirePermission(Permissions.Policy.AcknowledgementView);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/policies").WithTags("Policies");
        me.MapGet("/", async (PolicyService policies, CancellationToken ct) => TypedResults.Ok(await policies.MyPoliciesAsync(ct)))
            .WithName("ListMyPolicies").RequirePermission(Permissions.Self.PolicyView);
        me.MapGet("/{id:guid}", async (Guid id, PolicyService policies, CancellationToken ct) => TypedResults.Ok(await policies.MyPolicyAsync(id, ct)))
            .WithName("GetMyPolicy").RequirePermission(Permissions.Self.PolicyView);
        me.MapPost("/{id:guid}/acknowledge", async (Guid id, PolicyService policies, CancellationToken ct) =>
            {
                await policies.AcknowledgeAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("AcknowledgePolicy").RequirePermission(Permissions.Self.PolicyView);

        return endpoints;
    }
}
