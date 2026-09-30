using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateJobRequisitionBody(
    string Title,
    Guid DepartmentId,
    Guid DesignationId,
    Guid BranchId,
    int Openings,
    Guid? HiringManagerEmployeeId,
    string? Justification);

public sealed record JobRequisitionResponse(
    Guid Id,
    string RequisitionNo,
    string Title,
    Guid DepartmentId,
    Guid DesignationId,
    Guid BranchId,
    int Openings,
    Guid? HiringManagerEmployeeId,
    string? HiringManagerDisplayName,
    string? Justification,
    string Status);

public sealed record JobRequisitionListItem(
    Guid Id, string RequisitionNo, string Title, int Openings, string Status);

/// <summary>
/// Job requisitions - the first slice of Recruitment/ATS (architecture.md's Phase 3). State
/// transitions are their own routes, matching every other module in this codebase: Draft -> Open,
/// Open &lt;-&gt; OnHold, and Open/OnHold -> Closed or Cancelled.
/// </summary>
public static class JobRequisitionEndpoints
{
    public static IEndpointRouteBuilder MapJobRequisitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/recruitment/requisitions").WithTags("Recruitment");

        group.MapGet("/", ListAsync).WithName("ListJobRequisitions").RequirePermission(Permissions.Recruitment.RequisitionView);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionView);

        group.MapPost("/", CreateAsync).WithName("CreateJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/{id:guid}/open", OpenAsync).WithName("OpenJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/{id:guid}/hold", PutOnHoldAsync).WithName("HoldJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/{id:guid}/resume", ResumeAsync).WithName("ResumeJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/{id:guid}/close", CloseAsync).WithName("CloseJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelJobRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<JobRequisitionListItem>>> ListAsync(
        [AsParameters] PageQuery page, TalentDbContext db, CancellationToken ct)
    {
        PageRequest request = page.ToPageRequest();
        IQueryable<JobRequisition> query = db.JobRequisitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.RequisitionNo.Contains(term));
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(ct) : null;

        List<JobRequisitionListItem> items = await query
            .OrderByDescending(x => x.CreatedDate)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(x => new JobRequisitionListItem(x.Id, x.RequisitionNo, x.Title, x.Openings, x.Status.ToString()))
            .ToListAsync(ct);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Results<Ok<JobRequisitionResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        JobRequisition? entity = await db.JobRequisitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Created<JobRequisitionResponse>> CreateAsync(
        CreateJobRequisitionBody body, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        JobRequisition entity = await service.CreateAsync(
            body.Title, body.DepartmentId, body.DesignationId, body.BranchId,
            body.Openings, body.HiringManagerEmployeeId, body.Justification, ct);

        return TypedResults.Created($"/api/v1/recruitment/requisitions/{entity.Id}", await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Ok<JobRequisitionResponse>> OpenAsync(Guid id, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.OpenAsync(id, ct), db, ct));

    private static async Task<Ok<JobRequisitionResponse>> PutOnHoldAsync(Guid id, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.PutOnHoldAsync(id, ct), db, ct));

    private static async Task<Ok<JobRequisitionResponse>> ResumeAsync(Guid id, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.ResumeAsync(id, ct), db, ct));

    private static async Task<Ok<JobRequisitionResponse>> CloseAsync(Guid id, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.CloseAsync(id, ct), db, ct));

    private static async Task<Ok<JobRequisitionResponse>> CancelAsync(Guid id, JobRequisitionService service, TalentDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ToResponseAsync(await service.CancelAsync(id, ct), db, ct));

    private static async Task<JobRequisitionResponse> ToResponseAsync(JobRequisition entity, TalentDbContext db, CancellationToken ct)
    {
        string? hiringManagerDisplayName = null;

        if (entity.HiringManagerEmployeeId is { } managerId)
        {
            hiringManagerDisplayName = await db.EmployeeSnapshots.AsNoTracking()
                .Where(x => x.EmployeeId == managerId)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new JobRequisitionResponse(
            entity.Id, entity.RequisitionNo, entity.Title, entity.DepartmentId, entity.DesignationId, entity.BranchId,
            entity.Openings, entity.HiringManagerEmployeeId, hiringManagerDisplayName, entity.Justification, entity.Status.ToString());
    }
}
