using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateJobPostingBody(
    string Title,
    string DescriptionHtml,
    string? Location,
    bool IsInternal,
    bool IsPublic,
    string Slug,
    DateOnly? ClosesDate);

public sealed record JobPostingResponse(
    Guid Id,
    Guid RequisitionId,
    string Title,
    string DescriptionHtml,
    string? Location,
    bool IsInternal,
    bool IsPublic,
    string Slug,
    DateTime? PublishedDate,
    DateOnly? ClosesDate,
    string Status);

public sealed record JobPostingListItem(Guid Id, string Title, string Slug, bool IsPublic, string Status);

/// <summary>
/// Postings against a requisition - nested under it (<c>/requisitions/{requisitionId}/postings</c>)
/// since a posting never exists without one. No public careers page or external channel fan-out in
/// this v1 slice - see <see cref="Gggs.Talent.Domain.Recruitment.JobPosting"/>'s own remarks.
/// </summary>
public static class JobPostingEndpoints
{
    public static IEndpointRouteBuilder MapJobPostingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/api/v1/recruitment/requisitions/{requisitionId:guid}/postings")
            .WithTags("Recruitment");

        group.MapGet("/", ListAsync).WithName("ListJobPostings").RequirePermission(Permissions.Recruitment.PostingView);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetJobPosting").RequirePermission(Permissions.Recruitment.PostingView);

        group.MapPost("/", CreateAsync).WithName("CreateJobPosting").RequirePermission(Permissions.Recruitment.PostingManage);
        group.MapPost("/{id:guid}/publish", PublishAsync).WithName("PublishJobPosting").RequirePermission(Permissions.Recruitment.PostingManage);
        group.MapPost("/{id:guid}/close", CloseAsync).WithName("CloseJobPosting").RequirePermission(Permissions.Recruitment.PostingManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<JobPostingListItem>>> ListAsync(
        Guid requisitionId, TalentDbContext db, CancellationToken ct)
    {
        List<JobPostingListItem> items = await db.JobPostings.AsNoTracking()
            .Where(x => x.RequisitionId == requisitionId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new JobPostingListItem(x.Id, x.Title, x.Slug, x.IsPublic, x.Status.ToString()))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<JobPostingListItem>>(items);
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound>> GetAsync(
        Guid requisitionId, Guid id, TalentDbContext db, CancellationToken ct)
    {
        JobPosting? entity = await db.JobPostings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.RequisitionId == requisitionId, ct);

        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<JobPostingResponse>> CreateAsync(
        Guid requisitionId, CreateJobPostingBody body, JobPostingService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        JobPosting entity = await service.CreateAsync(
            requisitionId, body.Title, body.DescriptionHtml, body.Location,
            body.IsInternal, body.IsPublic, body.Slug, body.ClosesDate, ct);

        return TypedResults.Created(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound>> PublishAsync(
        Guid requisitionId, Guid id, JobPostingService service, TalentDbContext db, CancellationToken ct)
    {
        if (!await BelongsToRequisitionAsync(requisitionId, id, db, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToResponse(await service.PublishAsync(id, ct)));
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound>> CloseAsync(
        Guid requisitionId, Guid id, JobPostingService service, TalentDbContext db, CancellationToken ct)
    {
        if (!await BelongsToRequisitionAsync(requisitionId, id, db, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToResponse(await service.CloseAsync(id, ct)));
    }

    /// <summary>Guards against acting on a real posting id reached through the wrong parent
    /// requisition's URL - a 404, not a 403, matching this codebase's "existence is not leaked to a
    /// caller who does not already know it" convention.</summary>
    private static async Task<bool> BelongsToRequisitionAsync(Guid requisitionId, Guid postingId, TalentDbContext db, CancellationToken ct)
        => await db.JobPostings.AsNoTracking().AnyAsync(x => x.Id == postingId && x.RequisitionId == requisitionId, ct);

    private static JobPostingResponse ToResponse(JobPosting entity) => new(
        entity.Id, entity.RequisitionId, entity.Title, entity.DescriptionHtml, entity.Location,
        entity.IsInternal, entity.IsPublic, entity.Slug, entity.PublishedDate, entity.ClosesDate, entity.Status.ToString());
}
