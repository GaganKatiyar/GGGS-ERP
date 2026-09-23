using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Sample.Api.Domain;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Sample.Api.Endpoints;

/// <summary>Request to create a sample record. Note there is no TenantId: callers never name a tenant.</summary>
public sealed record CreateSampleRecordRequest(string Code, string Title, string? Notes);

public sealed record SampleRecordResponse(
    Guid Id,
    string Code,
    string Title,
    string? Notes,
    bool IsActive,
    DateTime CreatedDate,
    Guid CreatedBy);

/// <summary>
/// The endpoints the Phase 0 harness exercises: a tenant-scoped list, a get, and a create. Each one
/// declares the permission it needs; none of them mentions the tenant, because the tenant comes
/// from the token and is applied by the query filter and row-level security underneath.
/// </summary>
public static class SampleEndpoints
{
    public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/sample/records").WithTags("Sample");

        group.MapGet("/", ListAsync)
            .WithName("ListSampleRecords")
            .RequireAuthorization(RequirePermissionAttribute.BuildPolicyName([Permissions.Organization.View], PermissionMode.Any));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSampleRecord")
            .RequireAuthorization(RequirePermissionAttribute.BuildPolicyName([Permissions.Organization.View], PermissionMode.Any));

        group.MapPost("/", CreateAsync)
            .WithName("CreateSampleRecord")
            .RequireAuthorization(RequirePermissionAttribute.BuildPolicyName([Permissions.Organization.Manage], PermissionMode.Any));

        return endpoints;
    }

    private static async Task<Ok<PagedResult<SampleRecordResponse>>> ListAsync(
        [AsParameters] PageQuery page,
        SampleDbContext database,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();

        // No tenant predicate is written here on purpose: the global query filter adds it, and the
        // database's security policy enforces it regardless of what this query says.
        IQueryable<SampleRecord> query = database.SampleRecords.AsNoTracking();

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<SampleRecordResponse> items = await query
            .OrderByDescending(record => record.CreatedDate)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(record => ToResponse(record))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Ok<SampleRecordResponse>> GetAsync(
        Guid id,
        SampleDbContext database,
        CancellationToken cancellationToken)
    {
        SampleRecord? record = await database.SampleRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        // Another tenant's id is indistinguishable from a made-up one, which is the point.
        return record is null
            ? throw new NotFoundException(nameof(SampleRecord), id)
            : TypedResults.Ok(ToResponse(record));
    }

    private static async Task<Created<SampleRecordResponse>> CreateAsync(
        CreateSampleRecordRequest request,
        SampleDbContext database,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Normalized outside the expression: EF cannot translate a culture-aware comparison, and
        // doing it here means the database compares two plain values against the unique index.
        string normalizedCode = request.Code.Trim().ToUpperInvariant();

        bool codeTaken = await database.SampleRecords
            .AnyAsync(record => record.Code == normalizedCode, cancellationToken);

        if (codeTaken)
        {
            throw new DomainRuleException("SAMPLE_CODE_IN_USE", $"Code '{request.Code}' is already used in this tenant.");
        }

        SampleRecord record = SampleRecord.Create(request.Code, request.Title, request.Notes);
        database.SampleRecords.Add(record);

        // TenantId, CreatedBy and CreatedDate are stamped by the interceptor during this call.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/sample/records/{record.Id}", ToResponse(record));
    }

    private static SampleRecordResponse ToResponse(SampleRecord record) => new(
        record.Id,
        record.Code,
        record.Title,
        record.Notes,
        record.IsActive,
        record.CreatedDate,
        record.CreatedBy);
}
