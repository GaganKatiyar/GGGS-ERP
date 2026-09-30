using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record ApplyToPostingBody(
    string FirstName,
    string? LastName,
    string Email,
    string? Phone,
    string? CurrentEmployer,
    int? TotalExperienceMonths,
    string? Source);

public sealed record JobApplicationResponse(
    Guid Id,
    Guid PostingId,
    Guid CandidateId,
    string CandidateDisplayName,
    string CandidateEmail,
    string Stage,
    string? RejectionReason);

public sealed record JobApplicationListItem(
    Guid Id, Guid CandidateId, string CandidateDisplayName, string Stage);

public sealed record RejectApplicationBody(string? Reason);

/// <summary>
/// Applications - nested under their posting (<c>/postings/{postingId}/applications</c>) for
/// creation and listing, since that is how a recruiter always arrives at them; the stage
/// transitions (<c>/applications/{id}/advance</c>, <c>/reject</c>) act on the application by its
/// own id alone, since a recruiter working an applications board does not necessarily know or care
/// which posting produced the row currently in front of them.
/// <para>
/// This is the internal recruiter-entry path only - see <see cref="Candidate"/>'s own remarks on
/// what a public apply flow would still need (encryption, consent, resume upload), none of which
/// this v1 slice has.
/// </para>
/// </summary>
public static class JobApplicationEndpoints
{
    public static IEndpointRouteBuilder MapJobApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder postingGroup = endpoints
            .MapGroup("/api/v1/recruitment/postings/{postingId:guid}/applications")
            .WithTags("Recruitment");

        postingGroup.MapGet("/", ListAsync).WithName("ListJobApplications").RequirePermission(Permissions.Recruitment.ApplicationView);
        postingGroup.MapPost("/", ApplyAsync).WithName("ApplyToPosting").RequirePermission(Permissions.Recruitment.ApplicationManage);

        RouteGroupBuilder applicationGroup = endpoints
            .MapGroup("/api/v1/recruitment/applications")
            .WithTags("Recruitment");

        applicationGroup.MapGet("/{id:guid}", GetAsync).WithName("GetJobApplication").RequirePermission(Permissions.Recruitment.ApplicationView);
        applicationGroup.MapPost("/{id:guid}/advance", AdvanceAsync).WithName("AdvanceJobApplication").RequirePermission(Permissions.Recruitment.ApplicationManage);
        applicationGroup.MapPost("/{id:guid}/reject", RejectAsync).WithName("RejectJobApplication").RequirePermission(Permissions.Recruitment.ApplicationManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<JobApplicationListItem>>> ListAsync(
        Guid postingId, TalentDbContext db, CancellationToken ct)
    {
        List<JobApplicationListItem> items = await (
            from application in db.JobApplications.AsNoTracking()
            join candidate in db.Candidates.AsNoTracking() on application.CandidateId equals candidate.Id
            where application.PostingId == postingId
            orderby application.CreatedDate descending
            select new JobApplicationListItem(
                application.Id, candidate.Id, candidate.LastName == null ? candidate.FirstName : candidate.FirstName + " " + candidate.LastName,
                application.Stage.ToString()))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<JobApplicationListItem>>(items);
    }

    private static async Task<Created<JobApplicationResponse>> ApplyAsync(
        Guid postingId, ApplyToPostingBody body, JobApplicationService service, TalentDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        JobApplication entity = await service.ApplyAsync(
            postingId, body.FirstName, body.LastName, body.Email, body.Phone,
            body.CurrentEmployer, body.TotalExperienceMonths, body.Source, ct);

        return TypedResults.Created(
            $"/api/v1/recruitment/applications/{entity.Id}", await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Results<Ok<JobApplicationResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        JobApplication? entity = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Ok<JobApplicationResponse>> AdvanceAsync(
        Guid id, JobApplicationService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.AdvanceAsync(id, ct), db, ct));

    private static async Task<Ok<JobApplicationResponse>> RejectAsync(
        Guid id, RejectApplicationBody body, JobApplicationService service, TalentDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Ok(await ToResponseAsync(await service.RejectAsync(id, body.Reason, ct), db, ct));
    }

    private static async Task<JobApplicationResponse> ToResponseAsync(JobApplication entity, TalentDbContext db, CancellationToken ct)
    {
        Candidate candidate = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == entity.CandidateId, ct);

        return new JobApplicationResponse(
            entity.Id, entity.PostingId, entity.CandidateId, candidate.DisplayName, candidate.Email,
            entity.Stage.ToString(), entity.RejectionReason);
    }
}
