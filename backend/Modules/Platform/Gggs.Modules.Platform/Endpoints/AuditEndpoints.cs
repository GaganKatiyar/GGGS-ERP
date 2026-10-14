using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Audit;
using Gggs.Modules.Platform.Endpoints;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Endpoints;

/// <summary>
/// Read access to a tenant's own audit trail.
/// <para>
/// Like <see cref="TenantSettingEndpoints"/>, nothing here names a tenant: the global query filter
/// and row-level security both apply to <see cref="AppDbContext.AuditLogs"/>, so a caller only
/// ever sees their own tenant's rows regardless of what this code does or forgets to do.
/// </para>
/// <para>
/// No write endpoints exist here on purpose - see <see cref="AuditLog"/>'s own remarks on why the
/// only writer is the consumer, not this API.
/// </para>
/// </summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/audit").WithTags("Audit");

        group.MapGet("/logs", ListAsync)
            .WithName("ListAuditLogs")
            .RequirePermission(Permissions.Audit.LogView);

        group.MapGet("/logs/{id:guid}", GetAsync)
            .WithName("GetAuditLog")
            .RequirePermission(Permissions.Audit.LogView);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<AuditLogItem>>> ListAsync(
        [AsParameters] PageQuery page,
        [AsParameters] AuditLogQuery filter,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();
        IQueryable<AuditLog> query = database.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(log => log.EntityType == filter.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(log => log.EntityId == filter.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(log => log.Action == filter.Action);
        }

        if (!string.IsNullOrWhiteSpace(filter.Service))
        {
            query = query.Where(log => log.Service == filter.Service);
        }

        if (filter.UserId is { } userId)
        {
            query = query.Where(log => log.UserId == userId);
        }

        if (filter.FromDate is { } fromDate)
        {
            query = query.Where(log => log.OccurredDate >= fromDate);
        }

        if (filter.ToDate is { } toDate)
        {
            query = query.Where(log => log.OccurredDate <= toDate);
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<AuditLogItem> items = await query
            .OrderByDescending(log => log.OccurredDate)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(log => ToItem(log))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Results<Ok<AuditLogItem>, NotFound>> GetAsync(
        Guid id,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        AuditLog? entry = await database.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

        return entry is null ? TypedResults.NotFound() : TypedResults.Ok(ToItem(entry));
    }

    private static AuditLogItem ToItem(AuditLog log) => new(
        log.Id,
        log.OccurredDate,
        log.UserId,
        log.Service,
        log.Action,
        log.EntityType,
        log.EntityId,
        log.OldValuesJson,
        log.NewValuesJson,
        log.ChangedFieldsCsv,
        log.CorrelationId);
}
