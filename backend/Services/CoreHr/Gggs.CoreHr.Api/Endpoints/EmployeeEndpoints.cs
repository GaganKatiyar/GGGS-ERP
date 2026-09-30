using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Api.Endpoints;

public sealed record AssignmentBody(
    Guid LegalEntityId,
    Guid BranchId,
    Guid DepartmentId,
    Guid DesignationId,
    Guid? GradeId,
    Guid? CostCenterId,
    Guid? ReportingManagerId,
    EmploymentType EmploymentType,
    DateOnly? ProbationEndDate)
{
    public AssignmentDetails ToDetails() => new(
        LegalEntityId, BranchId, DepartmentId, DesignationId, GradeId, CostCenterId,
        ReportingManagerId, EmploymentType, ProbationEndDate);
}

public sealed record HireEmployeeBody(
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? WorkEmail,
    DateOnly DateOfJoining,
    AssignmentBody Assignment);

/// <summary>One row of a bulk hire request - organizational references are codes (e.g. "ACME01",
/// "ENG"), not GUIDs, since that is what a spreadsheet import actually has.</summary>
public sealed record BulkHireRowBody(
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? WorkEmail,
    DateOnly DateOfJoining,
    string LegalEntityCode,
    string BranchCode,
    string DepartmentCode,
    string DesignationCode,
    string? GradeCode,
    EmploymentType EmploymentType,
    DateOnly? ProbationEndDate);

public sealed record HireEmployeesBulkBody(IReadOnlyList<BulkHireRowBody> Rows);

public sealed record BulkHireRowResultResponse(int RowNumber, bool Succeeded, Guid? EmployeeId, string? ErrorCode, string? ErrorMessage);

public sealed record BulkHireResultResponse(int TotalRows, int SuccessCount, int FailureCount, IReadOnlyList<BulkHireRowResultResponse> Rows);

public sealed record UpdateEmployeeNameBody(string FirstName, string? MiddleName, string? LastName);

public sealed record ChangeAssignmentBody(DateOnly EffectiveFrom, AssignmentBody Assignment, string? Reason);

public sealed record ChangeReportingManagerBody(DateOnly EffectiveFrom, Guid? NewReportingManagerId, string? Reason);

public sealed record ConfirmEmployeeBody(DateOnly ConfirmationDate);

public sealed record ExitEmployeeBody(DateOnly ExitDate, bool WasAbsconded);

public sealed record RehireEmployeeBody(DateOnly DateOfJoining, AssignmentBody Assignment);

public sealed record EmployeeResponse(
    Guid Id,
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string DisplayName,
    string? WorkEmail,
    string Status,
    DateOnly DateOfJoining,
    DateOnly? OriginalHireDate,
    DateOnly? ConfirmationDate,
    DateOnly? ExitDate,
    Guid? UserId);

public sealed record EmployeeListItem(Guid Id, string EmployeeCode, string DisplayName, string Status, DateOnly DateOfJoining);

public sealed record EmploymentRecordResponse(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent,
    Guid LegalEntityId,
    Guid BranchId,
    Guid DepartmentId,
    Guid DesignationId,
    Guid? GradeId,
    Guid? CostCenterId,
    Guid? ReportingManagerId,
    EmploymentType EmploymentType,
    DateOnly? ProbationEndDate,
    string ChangeType,
    string? ChangeReason);

/// <summary>
/// Employees: the core record and the effective-dated employment history that carries every
/// organizational assignment.
/// <para>
/// State transitions are their own routes - hire, transfer, promote, confirm, exit - rather than a
/// generic PATCH, matching every other module in this codebase: "what happened" is part of the
/// record precisely because a diff cannot recover a reason.
/// </para>
/// </summary>
public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/employees").WithTags("Employees");

        group.MapGet("/", ListAsync).WithName("ListEmployees").RequirePermission(Permissions.Employee.View);
        group.MapGet("/my-team", ListMyTeamAsync).WithName("ListMyTeam").RequirePermission(Permissions.Employee.View);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetEmployee").RequirePermission(Permissions.Employee.View);
        group.MapGet("/{id:guid}/employment", GetEmploymentHistoryAsync)
            .WithName("GetEmploymentHistory")
            .RequirePermission(Permissions.Employee.HistoryView);

        group.MapPost("/", HireAsync).WithName("HireEmployee").RequirePermission(Permissions.Employee.Create);
        group.MapPost("/bulk", HireBulkAsync).WithName("HireEmployeesBulk").RequirePermission(Permissions.Employee.Create);
        group.MapPut("/{id:guid}/name", UpdateNameAsync).WithName("UpdateEmployeeName").RequirePermission(Permissions.Employee.Edit);

        group.MapPost("/{id:guid}/transfer", TransferAsync).WithName("TransferEmployee").RequirePermission(Permissions.Employee.EmploymentManage);
        group.MapPost("/{id:guid}/promote", PromoteAsync).WithName("PromoteEmployee").RequirePermission(Permissions.Employee.EmploymentManage);
        group.MapPost("/{id:guid}/change-manager", ChangeManagerAsync)
            .WithName("ChangeEmployeeReportingManager")
            .RequirePermission(Permissions.Employee.EmploymentManage);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync).WithName("ConfirmEmployee").RequirePermission(Permissions.Employee.EmploymentManage);
        group.MapPost("/{id:guid}/serve-notice", ServeNoticeAsync)
            .WithName("ServeEmployeeNotice")
            .RequirePermission(Permissions.Employee.EmploymentManage);
        group.MapPost("/{id:guid}/rehire", RehireAsync).WithName("RehireEmployee").RequirePermission(Permissions.Employee.EmploymentManage);

        group.MapPost("/{id:guid}/exit", ExitAsync).WithName("ExitEmployee").RequirePermission(Permissions.Employee.ExitManage);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<EmployeeListItem>>> ListAsync(
        [AsParameters] PageQuery page,
        CoreHrDbContext db,
        CancellationToken ct)
    {
        PageRequest request = page.ToPageRequest();
        IQueryable<Employee> query = db.Employees.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.Trim();
            query = query.Where(x => x.DisplayName.Contains(term) || x.EmployeeCode.Contains(term));
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(ct) : null;

        List<EmployeeListItem> items = await query
            .OrderBy(x => x.DisplayName)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(x => new EmployeeListItem(x.Id, x.EmployeeCode, x.DisplayName, x.Status.ToString(), x.DateOfJoining))
            .ToListAsync(ct);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    /// <summary>
    /// Everyone whose current employment record reports to the caller - `NAVIGATION`'s "My Team"
    /// entries (`/team`, `/team/attendance`, `/team/leave`) all key off this, each then asking
    /// their own module (Time) for the actual attendance/leave data per employee id. No pagination:
    /// a direct-reports list is bounded by how many people one manager realistically has, unlike
    /// the tenant-wide list `ListAsync` serves.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<EmployeeListItem>>> ListMyTeamAsync(
        ICurrentUser currentUser, CoreHrDbContext db, CancellationToken ct)
    {
        if (currentUser.EmployeeId is not { } managerId)
        {
            return TypedResults.Ok<IReadOnlyList<EmployeeListItem>>([]);
        }

        List<EmployeeListItem> items = await (
            from employee in db.Employees.AsNoTracking()
            join record in db.EmploymentRecords.AsNoTracking()
                on employee.Id equals record.EmployeeId
            where record.IsCurrent && record.ReportingManagerId == managerId
            orderby employee.DisplayName
            select new EmployeeListItem(employee.Id, employee.EmployeeCode, employee.DisplayName, employee.Status.ToString(), employee.DateOfJoining))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<EmployeeListItem>>(items);
    }

    private static async Task<Results<Ok<EmployeeResponse>, NotFound>> GetAsync(Guid id, CoreHrDbContext db, CancellationToken ct)
    {
        Employee? entity = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Ok<IReadOnlyList<EmploymentRecordResponse>>> GetEmploymentHistoryAsync(
        Guid id,
        CoreHrDbContext db,
        CancellationToken ct)
    {
        List<EmploymentRecordResponse> items = await db.EmploymentRecords.AsNoTracking()
            .Where(x => x.EmployeeId == id)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmploymentRecordResponse(
                x.Id, x.EffectiveFrom, x.EffectiveTo, x.IsCurrent,
                x.LegalEntityId, x.BranchId, x.DepartmentId, x.DesignationId, x.GradeId, x.CostCenterId,
                x.ReportingManagerId, x.EmploymentType, x.ProbationEndDate,
                x.ChangeType.ToString(), x.ChangeReason))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<EmploymentRecordResponse>>(items);
    }

    private static async Task<Created<EmployeeResponse>> HireAsync(HireEmployeeBody body, EmployeeService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Employee employee = await service.HireAsync(
            body.EmployeeCode, body.FirstName, body.MiddleName, body.LastName, body.WorkEmail,
            body.DateOfJoining, body.Assignment.ToDetails(), ct);

        return TypedResults.Created($"/api/v1/employees/{employee.Id}", ToResponse(employee));
    }

    private static async Task<Ok<BulkHireResultResponse>> HireBulkAsync(
        HireEmployeesBulkBody body,
        EmployeeBulkImportService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        IReadOnlyList<BulkHireRow> rows = body.Rows
            .Select((row, index) => new BulkHireRow(
                index + 1,
                row.EmployeeCode, row.FirstName, row.MiddleName, row.LastName, row.WorkEmail, row.DateOfJoining,
                row.LegalEntityCode, row.BranchCode, row.DepartmentCode, row.DesignationCode, row.GradeCode,
                row.EmploymentType, row.ProbationEndDate))
            .ToList();

        BulkHireResult result = await service.HireBulkAsync(rows, ct);

        return TypedResults.Ok(new BulkHireResultResponse(
            result.TotalRows, result.SuccessCount, result.FailureCount,
            result.Rows.Select(r => new BulkHireRowResultResponse(r.RowNumber, r.Succeeded, r.EmployeeId, r.ErrorCode, r.ErrorMessage)).ToList()));
    }

    private static async Task<Ok<EmployeeResponse>> UpdateNameAsync(
        Guid id,
        UpdateEmployeeNameBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Employee employee = await service.UpdateNameAsync(id, body.FirstName, body.MiddleName, body.LastName, ct);
        return TypedResults.Ok(ToResponse(employee));
    }

    private static async Task<Ok<EmploymentRecordResponse>> TransferAsync(
        Guid id,
        ChangeAssignmentBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        EmploymentRecord record = await service.TransferAsync(id, body.EffectiveFrom, body.Assignment.ToDetails(), body.Reason, ct);
        return TypedResults.Ok(ToResponse(record));
    }

    private static async Task<Ok<EmploymentRecordResponse>> PromoteAsync(
        Guid id,
        ChangeAssignmentBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        EmploymentRecord record = await service.PromoteAsync(id, body.EffectiveFrom, body.Assignment.ToDetails(), body.Reason, ct);
        return TypedResults.Ok(ToResponse(record));
    }

    private static async Task<Ok<EmploymentRecordResponse>> ChangeManagerAsync(
        Guid id,
        ChangeReportingManagerBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        EmploymentRecord record = await service.ChangeReportingManagerAsync(
            id, body.EffectiveFrom, body.NewReportingManagerId, body.Reason, ct);

        return TypedResults.Ok(ToResponse(record));
    }

    private static async Task<Ok<EmployeeResponse>> ConfirmAsync(
        Guid id,
        ConfirmEmployeeBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Employee employee = await service.ConfirmAsync(id, body.ConfirmationDate, ct);
        return TypedResults.Ok(ToResponse(employee));
    }

    private static async Task<Ok<EmployeeResponse>> ServeNoticeAsync(Guid id, EmployeeService service, CancellationToken ct)
    {
        Employee employee = await service.ServeNoticeAsync(id, ct);
        return TypedResults.Ok(ToResponse(employee));
    }

    private static async Task<Ok<EmployeeResponse>> RehireAsync(
        Guid id,
        RehireEmployeeBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Employee employee = await service.RehireAsync(id, body.DateOfJoining, body.Assignment.ToDetails(), ct);
        return TypedResults.Ok(ToResponse(employee));
    }

    private static async Task<Ok<EmployeeResponse>> ExitAsync(
        Guid id,
        ExitEmployeeBody body,
        EmployeeService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Employee employee = await service.ExitAsync(id, body.ExitDate, body.WasAbsconded, ct);
        return TypedResults.Ok(ToResponse(employee));
    }

    private static EmployeeResponse ToResponse(Employee entity) => new(
        entity.Id,
        entity.EmployeeCode,
        entity.FirstName,
        entity.MiddleName,
        entity.LastName,
        entity.DisplayName,
        entity.WorkEmail,
        entity.Status.ToString(),
        entity.DateOfJoining,
        entity.OriginalHireDate,
        entity.ConfirmationDate,
        entity.ExitDate,
        entity.UserId);

    private static EmploymentRecordResponse ToResponse(EmploymentRecord entity) => new(
        entity.Id,
        entity.EffectiveFrom,
        entity.EffectiveTo,
        entity.IsCurrent,
        entity.LegalEntityId,
        entity.BranchId,
        entity.DepartmentId,
        entity.DesignationId,
        entity.GradeId,
        entity.CostCenterId,
        entity.ReportingManagerId,
        entity.EmploymentType,
        entity.ProbationEndDate,
        entity.ChangeType.ToString(),
        entity.ChangeReason);
}
