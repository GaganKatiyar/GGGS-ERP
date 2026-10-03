using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Reporting.Api.Contracts;
using Gggs.Reporting.Application.Headcount;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Reporting.Api.Endpoints;

/// <summary>
/// The first slice of the real event-fed read model (architecture.md §12.2/§7.2's
/// <c>HeadcountDailySnapshot</c>) - built nightly from Core HR's own events rather than queried
/// live, unlike the existing <c>/api/v1/reports/headcount</c> point-in-time query this
/// deliberately does not replace (see that endpoint's own remarks on why it exists as a stopgap).
/// This one instead answers "what did headcount look like over time", which a live query cannot.
/// </summary>
public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/reporting/headcount-snapshots").WithTags("Reporting");

        group.MapGet("/", ListAsync).WithName("ListHeadcountSnapshots").RequirePermission(Permissions.Report.View);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<HeadcountDailySnapshotResponse>>> ListAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        IHeadcountDailySnapshotRepository snapshots,
        ITenantContext tenantContext,
        TimeProvider clock,
        CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        DateOnly rangeStart = fromDate ?? today.AddDays(-30);
        DateOnly rangeEnd = toDate ?? today;

        var rows = await snapshots.ListAsync(tenantContext.TenantId, rangeStart, rangeEnd, ct);

        IReadOnlyList<HeadcountDailySnapshotResponse> items = rows
            .Select(s => new HeadcountDailySnapshotResponse(
                s.SnapshotDate, s.LegalEntityId, s.BranchId, s.DepartmentId, s.Headcount, s.Joiners, s.Leavers))
            .ToList();

        return TypedResults.Ok(items);
    }
}
