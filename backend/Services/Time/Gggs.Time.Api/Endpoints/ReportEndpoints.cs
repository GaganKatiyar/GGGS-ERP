using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Api.Endpoints;

public sealed record AttendanceStatusCount(AttendanceDayStatus Status, int Days);

public sealed record AttendanceSummaryReportResponse(
    DateOnly FromDate, DateOnly ToDate, int TotalDays, IReadOnlyList<AttendanceStatusCount> ByStatus, int RegularizedDays);

public sealed record LeaveTypeSummary(Guid LeaveTypeId, string LeaveTypeName, decimal TakenDays, decimal PendingDays);

public sealed record LeaveSummaryReportResponse(int PeriodYear, IReadOnlyList<LeaveTypeSummary> ByType);

/// <summary>
/// Reporting v1 - same direct-read shape as Core HR's own <c>ReportEndpoints</c> (see its remarks):
/// no separate read model, queries this service's own tables for the period asked for.
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/reports").WithTags("Reports");

        group.MapGet("/attendance-summary", GetAttendanceSummaryAsync)
            .WithName("GetAttendanceSummaryReport")
            .RequirePermission(Permissions.Report.View);

        group.MapGet("/leave-summary", GetLeaveSummaryAsync)
            .WithName("GetLeaveSummaryReport")
            .RequirePermission(Permissions.Report.View);

        return endpoints;
    }

    /// <summary>Every computed <see cref="AttendanceDay"/> in the range, tenant-wide (not scoped to
    /// one employee - a manager wanting their own team's numbers filters client-side from
    /// <c>EmployeeReferenceApi.listMyTeam</c>, the same "no batch endpoint yet" trade-off
    /// <c>TeamAttendancePage</c> already makes).</summary>
    private static async Task<Ok<AttendanceSummaryReportResponse>> GetAttendanceSummaryAsync(
        DateOnly? fromDate, DateOnly? toDate, TimeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        DateOnly rangeStart = fromDate ?? new DateOnly(today.Year, today.Month, 1);
        DateOnly rangeEnd = toDate ?? today;

        IQueryable<AttendanceDay> days = db.AttendanceDays.AsNoTracking()
            .Where(x => x.AttendanceDate >= rangeStart && x.AttendanceDate <= rangeEnd);

        int totalDays = await days.CountAsync(ct);
        int regularizedDays = await days.CountAsync(x => x.IsRegularized, ct);

        List<AttendanceStatusCount> byStatus = await days
            .GroupBy(x => x.Status)
            .OrderBy(g => g.Key)
            .Select(g => new AttendanceStatusCount(g.Key, g.Count()))
            .ToListAsync(ct);

        return TypedResults.Ok(new AttendanceSummaryReportResponse(rangeStart, rangeEnd, totalDays, byStatus, regularizedDays));
    }

    /// <summary>Taken (approved debits, net of reversals) and pending days per leave type for the
    /// period year - the same two buckets <see cref="LeaveBalance"/> already tracks per employee,
    /// summed tenant-wide here instead of per employee.</summary>
    private static async Task<Ok<LeaveSummaryReportResponse>> GetLeaveSummaryAsync(
        int? periodYear, TimeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        int year = periodYear ?? clock.GetUtcNow().Year;

        List<LeaveTypeSummary> byType = await (
            from balance in db.LeaveBalances.AsNoTracking()
            where balance.PeriodYear == year
            join leaveType in db.LeaveTypes.AsNoTracking() on balance.LeaveTypeId equals leaveType.Id
            group new { balance.Taken, balance.PendingApproval } by new { leaveType.Id, leaveType.Name } into g
            orderby g.Key.Name
            select new LeaveTypeSummary(g.Key.Id, g.Key.Name, g.Sum(x => x.Taken), g.Sum(x => x.PendingApproval)))
            .ToListAsync(ct);

        return TypedResults.Ok(new LeaveSummaryReportResponse(year, byType));
    }
}
