using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Performance;
using Gggs.Talent.Domain.Performance;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreatePerformanceCycleBody(string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record PerformanceCycleResponse(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status);

public sealed record PerformanceCycleListItem(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status);

/// <summary>
/// Review periods - Draft -> Open -> Closed. Goals and reviews may only be raised while a cycle is
/// Open. No configurable phases in this v1 slice - see <see cref="PerformanceCycle"/>'s own remarks.
/// </summary>
public static class PerformanceCycleEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceCycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/performance/cycles").WithTags("Performance");

        group.MapGet("/", ListAsync).WithName("ListPerformanceCycles").RequirePermission(Permissions.Performance.CycleView);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetPerformanceCycle").RequirePermission(Permissions.Performance.CycleView);

        group.MapPost("/", CreateAsync).WithName("CreatePerformanceCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPost("/{id:guid}/open", OpenAsync).WithName("OpenPerformanceCycle").RequirePermission(Permissions.Performance.CycleManage);
        group.MapPost("/{id:guid}/close", CloseAsync).WithName("ClosePerformanceCycle").RequirePermission(Permissions.Performance.CycleManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<PerformanceCycleListItem>>> ListAsync(TalentDbContext db, CancellationToken ct)
    {
        List<PerformanceCycleListItem> items = await db.PerformanceCycles.AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .Select(x => new PerformanceCycleListItem(x.Id, x.Name, x.StartDate, x.EndDate, x.Status.ToString()))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PerformanceCycleListItem>>(items);
    }

    private static async Task<Results<Ok<PerformanceCycleResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        PerformanceCycle? entity = await db.PerformanceCycles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<PerformanceCycleResponse>> CreateAsync(
        CreatePerformanceCycleBody body, PerformanceCycleService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        PerformanceCycle entity = await service.CreateAsync(body.Name, body.StartDate, body.EndDate, ct);
        return TypedResults.Created($"/api/v1/performance/cycles/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<PerformanceCycleResponse>> OpenAsync(Guid id, PerformanceCycleService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.OpenAsync(id, ct)));

    private static async Task<Ok<PerformanceCycleResponse>> CloseAsync(Guid id, PerformanceCycleService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.CloseAsync(id, ct)));

    private static PerformanceCycleResponse ToResponse(PerformanceCycle entity)
        => new(entity.Id, entity.Name, entity.StartDate, entity.EndDate, entity.Status.ToString());
}
