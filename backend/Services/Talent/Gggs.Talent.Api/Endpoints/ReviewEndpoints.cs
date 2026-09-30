using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Performance;
using Gggs.Talent.Domain.Performance;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateReviewBody(Guid CycleId, Guid EmployeeId, Guid ManagerEmployeeId);

public sealed record SubmitReviewRatingBody(int Rating, string? Comments);

public sealed record ReviewResponse(
    Guid Id,
    Guid CycleId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    int? SelfRating,
    string? SelfComments,
    int? ManagerRating,
    string? ManagerComments,
    string Status,
    DateTime? ReleasedDate);

/// <summary>
/// A fixed self-then-manager review, 1-5 rating each - no configurable rating scale or review
/// template in this v1 slice, see <see cref="Review"/>'s own remarks.
/// </summary>
public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/performance/reviews").WithTags("Performance");

        group.MapGet("/{id:guid}", GetAsync).WithName("GetReview").RequirePermission(Permissions.Performance.ReviewView);
        group.MapPost("/", CreateAsync).WithName("CreateReview").RequirePermission(Permissions.Performance.ReviewManageOwn);
        group.MapPost("/{id:guid}/self", SubmitSelfReviewAsync).WithName("SubmitSelfReview").RequirePermission(Permissions.Performance.ReviewManageOwn);
        group.MapPost("/{id:guid}/manager", SubmitManagerReviewAsync).WithName("SubmitManagerReview").RequirePermission(Permissions.Performance.ReviewManageAsManager);
        group.MapPost("/{id:guid}/release", ReleaseAsync).WithName("ReleaseReview").RequirePermission(Permissions.Performance.ReviewManageAsManager);

        return endpoints;
    }

    private static async Task<Results<Ok<ReviewResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        Review? entity = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<ReviewResponse>> CreateAsync(CreateReviewBody body, ReviewService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Review entity = await service.CreateAsync(body.CycleId, body.EmployeeId, body.ManagerEmployeeId, ct);
        return TypedResults.Created($"/api/v1/performance/reviews/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<ReviewResponse>> SubmitSelfReviewAsync(
        Guid id, SubmitReviewRatingBody body, ReviewService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Ok(ToResponse(await service.SubmitSelfReviewAsync(id, body.Rating, body.Comments, ct)));
    }

    private static async Task<Ok<ReviewResponse>> SubmitManagerReviewAsync(
        Guid id, SubmitReviewRatingBody body, ReviewService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Ok(ToResponse(await service.SubmitManagerReviewAsync(id, body.Rating, body.Comments, ct)));
    }

    private static async Task<Ok<ReviewResponse>> ReleaseAsync(Guid id, ReviewService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReleaseAsync(id, ct)));

    private static ReviewResponse ToResponse(Review entity) => new(
        entity.Id, entity.CycleId, entity.EmployeeId, entity.ManagerEmployeeId,
        entity.SelfRating, entity.SelfComments, entity.ManagerRating, entity.ManagerComments,
        entity.Status.ToString(), entity.ReleasedDate);
}
