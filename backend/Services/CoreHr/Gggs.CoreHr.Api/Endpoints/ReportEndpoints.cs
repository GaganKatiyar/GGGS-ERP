using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Api.Endpoints;

public sealed record HeadcountByDepartmentItem(Guid DepartmentId, string DepartmentName, int Headcount);

public sealed record HeadcountByStatusItem(string Status, int Headcount);

public sealed record HeadcountReportResponse(
    int TotalActive,
    int Joiners,
    int Leavers,
    IReadOnlyList<HeadcountByDepartmentItem> ByDepartment,
    IReadOnlyList<HeadcountByStatusItem> ByStatus);

/// <summary>
/// Reporting v1 - architecture.md §22's own scoping note: a full event-fed read model is deferred
/// until either data volume or genuine cross-service reporting needs it (nothing publishes the
/// events such a model would consume yet - see this module's "as-built" remarks). Reads straight
/// off Core HR's own tables instead, the same way every other v1 report in this pass does.
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/reports").WithTags("Reports");

        group.MapGet("/headcount", GetHeadcountAsync).WithName("GetHeadcountReport").RequirePermission(Permissions.Report.View);

        return endpoints;
    }

    /// <summary>
    /// "Active" means every status short of <see cref="EmployeeStatus.Exited"/>/<see cref="EmployeeStatus.Absconded"/> -
    /// <see cref="EmployeeStatus.OnNotice"/> is still headcount until their exit actually lands.
    /// Joiners/leavers count within <paramref name="fromDate"/>/<paramref name="toDate"/> inclusive,
    /// defaulting to the current month when the caller passes neither.
    /// </summary>
    private static async Task<Ok<HeadcountReportResponse>> GetHeadcountAsync(
        DateOnly? fromDate, DateOnly? toDate, CoreHrDbContext db, TimeProvider clock, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        DateOnly rangeStart = fromDate ?? new DateOnly(today.Year, today.Month, 1);
        DateOnly rangeEnd = toDate ?? today;

        IQueryable<Employee> active = db.Employees.AsNoTracking()
            .Where(x => x.Status != EmployeeStatus.Exited && x.Status != EmployeeStatus.Absconded);

        int totalActive = await active.CountAsync(ct);

        int joiners = await db.Employees.AsNoTracking()
            .CountAsync(x => x.DateOfJoining >= rangeStart && x.DateOfJoining <= rangeEnd, ct);

        int leavers = await db.Employees.AsNoTracking()
            .CountAsync(x => x.ExitDate != null && x.ExitDate >= rangeStart && x.ExitDate <= rangeEnd, ct);

        List<HeadcountByDepartmentItem> byDepartment = await (
            from employee in active
            join record in db.EmploymentRecords.AsNoTracking() on employee.Id equals record.EmployeeId
            where record.IsCurrent
            join department in db.Departments.AsNoTracking() on record.DepartmentId equals department.Id
            group department by new { department.Id, department.Name } into g
            orderby g.Key.Name
            select new HeadcountByDepartmentItem(g.Key.Id, g.Key.Name, g.Count()))
            .ToListAsync(ct);

        List<HeadcountByStatusItem> byStatus = await db.Employees.AsNoTracking()
            .GroupBy(x => x.Status)
            .OrderBy(g => g.Key)
            .Select(g => new HeadcountByStatusItem(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        return TypedResults.Ok(new HeadcountReportResponse(totalActive, joiners, leavers, byDepartment, byStatus));
    }
}
