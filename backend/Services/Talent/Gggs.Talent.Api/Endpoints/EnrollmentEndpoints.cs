using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Learning;
using Gggs.Talent.Domain.Learning;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateEnrollmentBody(Guid CourseId, Guid EmployeeId, DateOnly? DueDate);

public sealed record CompleteEnrollmentBody(decimal? Score);

public sealed record EnrollmentResponse(
    Guid Id, Guid CourseId, Guid EmployeeId, DateOnly? DueDate, string Status, DateOnly? CompletedDate, decimal? Score);

public sealed record EnrollmentListItem(
    Guid Id, Guid CourseId, string CourseTitle, DateOnly? DueDate, string Status, DateOnly? CompletedDate, decimal? Score);

/// <summary>
/// An employee's assignments to courses - Assigned -&gt; InProgress -&gt; Completed, or Cancelled.
/// No training-session link in this v1 slice - see <see cref="Enrollment"/>'s own remarks.
/// </summary>
public static class EnrollmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder employeeGroup = endpoints.MapGroup("/api/v1/learning/employees/{employeeId:guid}/enrollments").WithTags("Learning");

        employeeGroup.MapGet("/", ListForEmployeeAsync).WithName("ListEmployeeEnrollments").RequirePermission(Permissions.Learning.EnrollmentView);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/learning/enrollments").WithTags("Learning");

        group.MapGet("/{id:guid}", GetAsync).WithName("GetEnrollment").RequirePermission(Permissions.Learning.EnrollmentView);
        group.MapPost("/", CreateAsync).WithName("CreateEnrollment").RequirePermission(Permissions.Learning.EnrollmentManage);
        group.MapPost("/{id:guid}/start", StartAsync).WithName("StartEnrollment").RequirePermission(Permissions.Learning.EnrollmentManageOwn);
        group.MapPost("/{id:guid}/complete", CompleteAsync).WithName("CompleteEnrollment").RequirePermission(Permissions.Learning.EnrollmentManageOwn);
        group.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelEnrollment").RequirePermission(Permissions.Learning.EnrollmentManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<EnrollmentListItem>>> ListForEmployeeAsync(
        Guid employeeId, TalentDbContext db, CancellationToken ct)
    {
        List<EnrollmentListItem> items = await db.Enrollments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Join(db.Courses.AsNoTracking(), e => e.CourseId, c => c.Id, (e, c) => new { Enrollment = e, CourseTitle = c.Title })
            .OrderByDescending(x => x.Enrollment.CreatedDate)
            .Select(x => new EnrollmentListItem(
                x.Enrollment.Id, x.Enrollment.CourseId, x.CourseTitle, x.Enrollment.DueDate,
                x.Enrollment.Status.ToString(), x.Enrollment.CompletedDate, x.Enrollment.Score))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<EnrollmentListItem>>(items);
    }

    private static async Task<Results<Ok<EnrollmentResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        Enrollment? entity = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<EnrollmentResponse>> CreateAsync(
        CreateEnrollmentBody body, EnrollmentService service, ICurrentUser currentUser, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Enrollment entity = await service.CreateAsync(body.CourseId, body.EmployeeId, currentUser.UserId, body.DueDate, ct);
        return TypedResults.Created($"/api/v1/learning/enrollments/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<EnrollmentResponse>> StartAsync(Guid id, EnrollmentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.StartAsync(id, ct)));

    private static async Task<Ok<EnrollmentResponse>> CompleteAsync(
        Guid id, CompleteEnrollmentBody body, EnrollmentService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Ok(ToResponse(await service.CompleteAsync(id, body.Score, ct)));
    }

    private static async Task<Ok<EnrollmentResponse>> CancelAsync(Guid id, EnrollmentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.CancelAsync(id, ct)));

    private static EnrollmentResponse ToResponse(Enrollment entity) => new(
        entity.Id, entity.CourseId, entity.EmployeeId, entity.DueDate, entity.Status.ToString(), entity.CompletedDate, entity.Score);
}
