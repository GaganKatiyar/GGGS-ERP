using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Performance;
using Gggs.Talent.Domain.Performance;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateGoalBody(
    Guid CycleId, Guid EmployeeId, string Title, string? Description, decimal TargetValue, string Unit, DateOnly DueDate);

public sealed record CheckInGoalBody(DateOnly CheckInDate, decimal Value, string? Comment);

public sealed record GoalResponse(
    Guid Id, Guid CycleId, Guid EmployeeId, string Title, string? Description,
    decimal TargetValue, decimal CurrentValue, string Unit, DateOnly DueDate, string Status);

public sealed record GoalListItem(Guid Id, string Title, decimal TargetValue, decimal CurrentValue, string Unit, string Status);

public sealed record GoalCheckInResponse(Guid Id, DateOnly CheckInDate, decimal Value, string? Comment);

/// <summary>
/// Goals against a cycle, and the check-ins that record progress on them. No KPI library reference
/// or cascading parent goal in this v1 slice - see <see cref="Goal"/>'s own remarks.
/// </summary>
public static class GoalEndpoints
{
    public static IEndpointRouteBuilder MapGoalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder employeeGroup = endpoints.MapGroup("/api/v1/performance/employees/{employeeId:guid}/goals").WithTags("Performance");

        employeeGroup.MapGet("/", ListForEmployeeAsync).WithName("ListEmployeeGoals").RequirePermission(Permissions.Performance.GoalManageOwn);

        RouteGroupBuilder goalGroup = endpoints.MapGroup("/api/v1/performance/goals").WithTags("Performance");

        goalGroup.MapGet("/{id:guid}", GetAsync).WithName("GetGoal").RequirePermission(Permissions.Performance.GoalManageOwn);
        goalGroup.MapPost("/", CreateAsync).WithName("CreateGoal").RequirePermission(Permissions.Performance.GoalManageOwn);
        goalGroup.MapPost("/{id:guid}/check-ins", CheckInAsync).WithName("CheckInGoal").RequirePermission(Permissions.Performance.GoalManageOwn);
        goalGroup.MapGet("/{id:guid}/check-ins", ListCheckInsAsync).WithName("ListGoalCheckIns").RequirePermission(Permissions.Performance.GoalManageOwn);
        goalGroup.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelGoal").RequirePermission(Permissions.Performance.GoalManageOwn);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<GoalListItem>>> ListForEmployeeAsync(
        Guid employeeId, TalentDbContext db, CancellationToken ct)
    {
        List<GoalListItem> items = await db.Goals.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new GoalListItem(x.Id, x.Title, x.TargetValue, x.CurrentValue, x.Unit, x.Status.ToString()))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<GoalListItem>>(items);
    }

    private static async Task<Results<Ok<GoalResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        Goal? entity = await db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<GoalResponse>> CreateAsync(CreateGoalBody body, GoalService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Goal entity = await service.CreateAsync(
            body.CycleId, body.EmployeeId, body.Title, body.Description, body.TargetValue, body.Unit, body.DueDate, ct);

        return TypedResults.Created($"/api/v1/performance/goals/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<GoalResponse>> CheckInAsync(Guid id, CheckInGoalBody body, GoalService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Ok(ToResponse(await service.CheckInAsync(id, body.CheckInDate, body.Value, body.Comment, ct)));
    }

    private static async Task<Ok<IReadOnlyList<GoalCheckInResponse>>> ListCheckInsAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        List<GoalCheckInResponse> items = await db.GoalCheckIns.AsNoTracking()
            .Where(x => x.GoalId == id)
            .OrderByDescending(x => x.CheckInDate)
            .Select(x => new GoalCheckInResponse(x.Id, x.CheckInDate, x.Value, x.Comment))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<GoalCheckInResponse>>(items);
    }

    private static async Task<Ok<GoalResponse>> CancelAsync(Guid id, GoalService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.CancelAsync(id, ct)));

    private static GoalResponse ToResponse(Goal entity) => new(
        entity.Id, entity.CycleId, entity.EmployeeId, entity.Title, entity.Description,
        entity.TargetValue, entity.CurrentValue, entity.Unit, entity.DueDate, entity.Status.ToString());
}
