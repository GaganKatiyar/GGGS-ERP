using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Resources.Application;
using Gggs.Modules.Resources.Domain;

namespace Gggs.Modules.Resources.Endpoints;

/// <summary>
/// Resource management under <c>/api/v1/resources</c>, with requests at <c>/api/v1/resource-requests</c>
/// and allocations at <c>/api/v1/resource-allocations</c> (spec §128).
/// </summary>
internal static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/resources").WithTags("Resources");

        group.MapGet("/", async ([AsParameters] ResourceListQuery query, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.ListAsync(query, ct)))
            .WithName("ListResources").RequirePermission(Permissions.Resource.ProfileView);
        group.MapGet("/dashboard", async (ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.DashboardAsync(ct)))
            .WithName("ResourceDashboard").RequirePermission(Permissions.Resource.AllocationView);
        group.MapGet("/calendar", async ([AsParameters] CalendarQuery query, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.CalendarAsync(query, ct)))
            .WithName("ResourceCalendar").RequirePermission(Permissions.Resource.AllocationView);
        group.MapGet("/bench", async (ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.BenchAsync(ct)))
            .WithName("ResourceBench").RequirePermission(Permissions.Resource.ProfileView);
        group.MapGet("/forecast", async (int? months, ResourceInsights insights, CancellationToken ct) => TypedResults.Ok(await insights.ForecastAsync(months ?? 6, ct)))
            .WithName("ResourceForecast").RequirePermission(Permissions.Resource.AllocationView);
        group.MapGet("/utilization", async (DateOnly? from, DateOnly? to, UtilizationGrouping? groupBy, Guid? departmentId, ResourceInsights insights, CancellationToken ct) =>
                TypedResults.Ok(await insights.UtilizationAsync(from, to, groupBy ?? UtilizationGrouping.Employee, departmentId, ct)))
            .WithName("ResourceUtilization").RequirePermission(Permissions.Resource.UtilizationView);
        group.MapGet("/{employeeId:guid}", async (Guid employeeId, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.ProfileAsync(employeeId, ct)))
            .WithName("ResourceProfile").RequirePermission(Permissions.Resource.ProfileView);
        group.MapPut("/{employeeId:guid}/profile", async (Guid employeeId, ProfileBody body, ResourceService resources, CancellationToken ct) =>
                TypedResults.Ok(await resources.SaveProfileAsync(employeeId, body, ct)))
            .WithName("SaveResourceProfile").RequirePermission(Permissions.Resource.ProfileManage);

        group.MapGet("/transfers", async (TransferStatus? status, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.TransfersAsync(status, ct)))
            .WithName("ListResourceTransfers").RequirePermission(Permissions.Resource.AllocationView);
        group.MapPost("/transfers", async (TransferBody body, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.RequestTransferAsync(body, ct)))
            .WithName("RequestResourceTransfer").RequirePermission(Permissions.Resource.AllocationManage);
        group.MapPost("/transfers/{id:guid}/cancel", async (Guid id, ResourceService resources, CancellationToken ct) =>
            {
                await resources.CancelTransferAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelResourceTransfer").RequirePermission(Permissions.Resource.AllocationManage);

        RouteGroupBuilder allocations = endpoints.MapGroup("/api/v1/resource-allocations").WithTags("Resources");
        allocations.MapPost("/", async (AllocateBody body, ResourceService resources, CancellationToken ct) => TypedResults.Ok(await resources.AllocateAsync(body, ct)))
            .WithName("AllocateResource").RequirePermission(Permissions.Resource.AllocationManage);
        allocations.MapPut("/{memberId:guid}", async (Guid memberId, UpdateAllocationBody body, ResourceService resources, CancellationToken ct) =>
                TypedResults.Ok(await resources.UpdateAllocationAsync(memberId, body, ct)))
            .WithName("UpdateResourceAllocation").RequirePermission(Permissions.Resource.AllocationManage);
        allocations.MapPost("/{memberId:guid}/release", async (Guid memberId, ReleaseAllocationBody body, ResourceService resources, CancellationToken ct) =>
            {
                await resources.ReleaseAsync(memberId, body.On, ct);
                return TypedResults.NoContent();
            })
            .WithName("ReleaseResourceAllocation").RequirePermission(Permissions.Resource.AllocationManage);

        RouteGroupBuilder requests = endpoints.MapGroup("/api/v1/resource-requests").WithTags("Resources");
        requests.MapGet("/", async (RequestStatus? status, Guid? projectId, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(status, projectId, ct)))
            .WithName("ListResourceRequests").RequirePermission(Permissions.Resource.RequestView);
        requests.MapGet("/{id:guid}", async (Guid id, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .WithName("GetResourceRequest").RequirePermission(Permissions.Resource.RequestView);
        requests.MapPost("/", async (RequestBody body, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(null, body, ct)))
            .WithName("RaiseResourceRequest").RequirePermission(Permissions.Resource.RequestManage);
        requests.MapPut("/{id:guid}", async (Guid id, RequestBody body, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(id, body, ct)))
            .WithName("UpdateResourceRequest").RequirePermission(Permissions.Resource.RequestManage);
        requests.MapPost("/{id:guid}/cancel", async (Guid id, RequestService service, CancellationToken ct) =>
            {
                await service.CancelAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelResourceRequest").RequirePermission(Permissions.Resource.RequestManage);
        requests.MapGet("/{id:guid}/matches", async (Guid id, int? take, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.MatchAsync(id, take ?? 20, ct)))
            .WithName("MatchResourceRequest").RequirePermission(Permissions.Resource.RequestView);
        requests.MapPost("/{id:guid}/candidates", async (Guid id, ShortlistBody body, RequestService service, CancellationToken ct) => TypedResults.Ok(await service.ShortlistAsync(id, body, ct)))
            .WithName("ShortlistCandidate").RequirePermission(Permissions.Resource.RequestManage);
        requests.MapPost("/{id:guid}/candidates/{candidateId:guid}/reject", async (Guid id, Guid candidateId, RejectCandidateBody body, RequestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RejectCandidateAsync(id, candidateId, body.Note, ct)))
            .WithName("RejectCandidate").RequirePermission(Permissions.Resource.RequestManage);
        requests.MapPost("/{id:guid}/candidates/{candidateId:guid}/allocate", async (Guid id, Guid candidateId, AllocateCandidateBody body, RequestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AllocateCandidateAsync(id, candidateId, body, ct)))
            .WithName("AllocateCandidate").RequirePermission(Permissions.Resource.RequestManage, Permissions.Resource.AllocationManage);

        return endpoints;
    }
}
