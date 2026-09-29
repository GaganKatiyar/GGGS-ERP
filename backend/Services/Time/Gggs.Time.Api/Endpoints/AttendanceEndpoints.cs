using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Application.Attendance;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Api.Endpoints;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapShiftEndpoints(endpoints);
        MapRosterEndpoints(endpoints);
        MapPunchEndpoints(endpoints);
        MapDayEndpoints(endpoints);
        MapRegularizationEndpoints(endpoints);

        return endpoints;
    }

    private static void MapShiftEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance/shifts").WithTags("Attendance");

        group.MapGet("/", async (TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<ShiftResponse> items = await database.Shifts.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => ToResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListShifts").RequirePermission(Permissions.Attendance.View);

        group.MapPost("/", async (CreateShiftBody body, ShiftService service, CancellationToken cancellationToken) =>
        {
            Shift shift = await service.CreateAsync(
                body.Code, body.Name, body.StartTime, body.EndTime, body.BreakMinutes, body.IsNightShift,
                body.GraceInMinutes, body.GraceOutMinutes, body.FullDayMinMinutes, body.HalfDayMinMinutes, cancellationToken);

            return TypedResults.Created($"/api/v1/attendance/shifts/{shift.Id}", ToResponse(shift));
        }).WithName("CreateShift").RequirePermission(Permissions.Attendance.ShiftManage);
    }

    private static void MapRosterEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance/rosters").WithTags("Attendance");

        group.MapGet("/", async (
            [FromQuery] Guid employeeId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<ShiftRosterResponse> items = await database.ShiftRosters.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.RosterDate >= fromDate && x.RosterDate <= toDate)
                .OrderBy(x => x.RosterDate)
                .Select(x => new ShiftRosterResponse(x.Id, x.EmployeeId, x.RosterDate, x.ShiftId, x.IsWeeklyOff, x.Source))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListShiftRoster").RequirePermission(Permissions.Attendance.View);

        group.MapPost("/", async (AssignShiftRosterBody body, ShiftRosterService service, CancellationToken cancellationToken) =>
        {
            ShiftRoster roster = await service.AssignAsync(body.EmployeeId, body.RosterDate, body.ShiftId, cancellationToken);
            return TypedResults.Created(
                $"/api/v1/attendance/rosters/{roster.Id}",
                new ShiftRosterResponse(roster.Id, roster.EmployeeId, roster.RosterDate, roster.ShiftId, roster.IsWeeklyOff, roster.Source));
        }).WithName("AssignShiftRoster").RequirePermission(Permissions.Attendance.RosterManage);

        group.MapPost("/weekly-off", async (AssignWeeklyOffBody body, ShiftRosterService service, CancellationToken cancellationToken) =>
        {
            ShiftRoster roster = await service.AssignWeeklyOffAsync(body.EmployeeId, body.RosterDate, cancellationToken);
            return TypedResults.Created(
                $"/api/v1/attendance/rosters/{roster.Id}",
                new ShiftRosterResponse(roster.Id, roster.EmployeeId, roster.RosterDate, roster.ShiftId, roster.IsWeeklyOff, roster.Source));
        }).WithName("AssignWeeklyOff").RequirePermission(Permissions.Attendance.RosterManage);
    }

    private static void MapPunchEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance/punches").WithTags("Attendance");

        group.MapPost("/", async (
            RecordPunchBody body, AttendancePunchService service, ICurrentUser currentUser, IPermissionProvider permissions,
            CancellationToken cancellationToken) =>
        {
            if (body.EmployeeId is { } requestedEmployeeId && requestedEmployeeId != currentUser.EmployeeId)
            {
                PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

                if (!held.Has(Permissions.Attendance.Manage))
                {
                    return Results.Forbid();
                }
            }

            AttendancePunch punch = await service.RecordAsync(
                body.EmployeeId, body.PunchTimeUtc, body.Direction, body.Source, body.DeviceId, body.Latitude, body.Longitude,
                ipAddress: null, body.ExternalRef, cancellationToken);

            return Results.Created($"/api/v1/attendance/punches/{punch.Id}", ToResponse(punch));
        }).WithName("RecordPunch").RequirePermission(Permissions.Self.AttendancePunch);

        group.MapPost("/me/check-in", async (
            AttendancePunchService service, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            AttendancePunch punch = await service.RecordAsync(
                employeeId: null, clock.GetUtcNow().UtcDateTime, PunchDirection.In, PunchSource.Web,
                deviceId: null, latitude: null, longitude: null, ipAddress: null, externalRef: null, cancellationToken);

            return Results.Created($"/api/v1/attendance/punches/{punch.Id}", ToResponse(punch));
        }).WithName("CheckIn").RequirePermission(Permissions.Self.AttendancePunch);

        group.MapPost("/me/check-out", async (
            AttendancePunchService service, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            AttendancePunch punch = await service.RecordAsync(
                employeeId: null, clock.GetUtcNow().UtcDateTime, PunchDirection.Out, PunchSource.Web,
                deviceId: null, latitude: null, longitude: null, ipAddress: null, externalRef: null, cancellationToken);

            return Results.Created($"/api/v1/attendance/punches/{punch.Id}", ToResponse(punch));
        }).WithName("CheckOut").RequirePermission(Permissions.Self.AttendancePunch);
    }

    private static void MapDayEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance/days").WithTags("Attendance");

        group.MapGet("/", async (
            [FromQuery] Guid employeeId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<AttendanceDayResponse> items = await database.AttendanceDays.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate)
                .OrderBy(x => x.AttendanceDate)
                .Select(x => ToResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListAttendanceDays").RequirePermission(Permissions.Attendance.View);

        group.MapGet("/me", async (
            [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            TimeDbContext database, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            if (currentUser.EmployeeId is not { } employeeId)
            {
                return Results.Ok(Array.Empty<AttendanceDayResponse>());
            }

            List<AttendanceDayResponse> items = await database.AttendanceDays.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate)
                .OrderBy(x => x.AttendanceDate)
                .Select(x => ToResponse(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        }).WithName("GetMyAttendanceDays").RequirePermission(Permissions.Self.AttendanceView);
    }

    private static void MapRegularizationEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance/regularizations").WithTags("Attendance");

        group.MapGet("/", async (
            [AsParameters] PageQuery page, [FromQuery] Guid? employeeId, TimeDbContext database, ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            PageRequest request = page.ToPageRequest();

            // Same self-scoping default as LeaveEndpoints.MapLeaveRequestEndpoints: no employeeId
            // and no elevated view permission falls back to the caller's own requests.
            Guid? scopedEmployeeId = employeeId ?? currentUser.EmployeeId;

            IQueryable<AttendanceRegularization> query = database.AttendanceRegularizations.AsNoTracking();

            if (scopedEmployeeId is { } id)
            {
                query = query.Where(x => x.EmployeeId == id);
            }

            long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

            List<AttendanceRegularizationResponse> items = await query
                .OrderByDescending(x => x.AttendanceDate)
                .Skip(request.Skip)
                .Take(request.PageSize)
                .Select(x => ToResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(PagedResult.From(items, request, total));
        }).WithName("ListAttendanceRegularizations").RequirePermission(Permissions.Attendance.RegularizationView);

        group.MapPost("/", async (
            SubmitRegularizationBody body, AttendanceRegularizationService service, ICurrentUser currentUser,
            IPermissionProvider permissions, CancellationToken cancellationToken) =>
        {
            if (body.EmployeeId is { } requestedEmployeeId && requestedEmployeeId != currentUser.EmployeeId)
            {
                PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

                if (!held.Has(Permissions.Attendance.Manage))
                {
                    return Results.Forbid();
                }
            }

            AttendanceRegularization regularization = await service.SubmitAsync(
                body.EmployeeId, body.AttendanceDate, body.RequestedIn, body.RequestedOut, body.ReasonCode, body.Remarks,
                cancellationToken);

            return Results.Created($"/api/v1/attendance/regularizations/{regularization.Id}", ToResponse(regularization));
        }).WithName("SubmitAttendanceRegularization").RequirePermission(Permissions.Attendance.RegularizationCreate);

        group.MapPost("/{id:guid}/cancel", async (
            Guid id, AttendanceRegularizationService service, CancellationToken cancellationToken) =>
        {
            await service.CancelAsync(id, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("CancelAttendanceRegularization").RequirePermission(Permissions.Attendance.RegularizationCreate);
    }

    private static ShiftResponse ToResponse(Shift entity) => new(
        entity.Id, entity.Code, entity.Name, entity.StartTime, entity.EndTime, entity.BreakMinutes, entity.IsNightShift,
        entity.GraceInMinutes, entity.GraceOutMinutes, entity.FullDayMinMinutes, entity.HalfDayMinMinutes);

    private static AttendancePunchResponse ToResponse(AttendancePunch entity) => new(
        entity.Id, entity.EmployeeId, entity.PunchTimeUtc, entity.Direction, entity.Source, entity.IsValid);

    private static AttendanceDayResponse ToResponse(AttendanceDay entity) => new(
        entity.Id, entity.EmployeeId, entity.AttendanceDate, entity.ShiftId, entity.FirstIn, entity.LastOut,
        entity.WorkedMinutes, entity.LateMinutes, entity.EarlyOutMinutes, entity.Status, entity.LeaveRequestId,
        entity.IsRegularized, entity.IsLocked);

    private static AttendanceRegularizationResponse ToResponse(AttendanceRegularization entity) => new(
        entity.Id, entity.EmployeeId, entity.AttendanceDate, entity.RequestedIn, entity.RequestedOut, entity.ReasonCode,
        entity.Remarks, entity.Status, entity.WorkflowInstanceId, entity.CreatedDate);
}
