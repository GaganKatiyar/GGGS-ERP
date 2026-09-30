using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Learning;
using Gggs.Talent.Domain.Learning;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateCourseBody(string Code, string Title, string? Description, decimal DurationHours);

public sealed record CourseResponse(Guid Id, string Code, string Title, string? Description, decimal DurationHours, bool IsActive);

public sealed record CourseListItem(Guid Id, string Code, string Title, decimal DurationHours, bool IsActive);

/// <summary>
/// The course catalog. No session scheduling, content link or certification link in this v1 slice -
/// see <see cref="Course"/>'s own remarks.
/// </summary>
public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/learning/courses").WithTags("Learning");

        group.MapGet("/", ListAsync).WithName("ListCourses").RequirePermission(Permissions.Learning.CourseView);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetCourse").RequirePermission(Permissions.Learning.CourseView);
        group.MapPost("/", CreateAsync).WithName("CreateCourse").RequirePermission(Permissions.Learning.CourseManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync).WithName("DeactivateCourse").RequirePermission(Permissions.Learning.CourseManage);
        group.MapPost("/{id:guid}/reactivate", ReactivateAsync).WithName("ReactivateCourse").RequirePermission(Permissions.Learning.CourseManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<CourseListItem>>> ListAsync(TalentDbContext db, CancellationToken ct)
    {
        List<CourseListItem> items = await db.Courses.AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(x => new CourseListItem(x.Id, x.Code, x.Title, x.DurationHours, x.IsActive))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<CourseListItem>>(items);
    }

    private static async Task<Results<Ok<CourseResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        Course? entity = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<CourseResponse>> CreateAsync(CreateCourseBody body, CourseService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Course entity = await service.CreateAsync(body.Code, body.Title, body.Description, body.DurationHours, ct);
        return TypedResults.Created($"/api/v1/learning/courses/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<CourseResponse>> DeactivateAsync(Guid id, CourseService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)));

    private static async Task<Ok<CourseResponse>> ReactivateAsync(Guid id, CourseService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)));

    private static CourseResponse ToResponse(Course entity)
        => new(entity.Id, entity.Code, entity.Title, entity.Description, entity.DurationHours, entity.IsActive);
}
