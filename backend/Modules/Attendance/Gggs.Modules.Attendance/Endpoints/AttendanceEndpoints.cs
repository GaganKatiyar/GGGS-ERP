using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Attendance.Application;
using Gggs.Modules.Attendance.Domain;

namespace Gggs.Modules.Attendance.Endpoints;

public sealed record RecomputeRequest(DateOnly From, DateOnly To, Guid? EmployeeId, Guid? DepartmentId);

public sealed record RecomputeResult(int Days);

/// <summary>Attendance (spec §18), and each employee's own under <c>/api/v1/me/attendance</c>.</summary>
internal static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/attendance").WithTags("Attendance");

        group.MapGet("/days", async (DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, AttendanceService attendance, CancellationToken ct) =>
            {
                IReadOnlyList<Guid> people = await attendance.PeopleAsync(Permissions.Attendance.View, departmentId, employeeId, ct);
                return TypedResults.Ok(await attendance.DaysAsync(people, from, to, ct));
            })
            .WithName("ListAttendanceDays").RequirePermission(Permissions.Attendance.View);
        group.MapGet("/summary", async (DateOnly? date, Guid? departmentId, AttendanceService attendance, ITenantClock clock, CancellationToken ct) =>
            {
                IReadOnlyList<Guid> people = await attendance.PeopleAsync(Permissions.Attendance.View, departmentId, null, ct);
                return TypedResults.Ok(await attendance.SummaryAsync(date ?? await clock.TodayAsync(ct), people, ct));
            })
            .WithName("AttendanceSummary").RequirePermission(Permissions.Attendance.View);
        group.MapPost("/recompute", async (RecomputeRequest body, AttendanceService attendance, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                IReadOnlyList<Guid> people = await attendance.PeopleAsync(Permissions.Attendance.Manage, body.DepartmentId, body.EmployeeId, ct);
                return TypedResults.Ok(new RecomputeResult(await attendance.PersistAsync(people, body.From, body.To, ct)));
            })
            .WithName("RecomputeAttendance").RequirePermission(Permissions.Attendance.Manage);

        group.MapGet("/regularizations", async (RegularizationStatus? status, AttendanceService attendance, CancellationToken ct) =>
                TypedResults.Ok(await attendance.RegularizationsAsync(mine: false, status, ct)))
            .WithName("ListRegularizations").RequirePermission(Permissions.Attendance.RegularizationView);

        group.MapGet("/devices", async (AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.DevicesAsync(ct)))
            .WithName("ListAttendanceDevices").RequirePermission(Permissions.Attendance.DeviceManage);
        group.MapPost("/devices", async (DeviceRequest body, AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.SaveDeviceAsync(null, body, ct)))
            .WithName("CreateAttendanceDevice").RequirePermission(Permissions.Attendance.DeviceManage);
        group.MapPut("/devices/{id:guid}", async (Guid id, DeviceRequest body, AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.SaveDeviceAsync(id, body, ct)))
            .WithName("UpdateAttendanceDevice").RequirePermission(Permissions.Attendance.DeviceManage);
        group.MapPost("/devices/{id:guid}/activate", async (Guid id, AttendanceService attendance, CancellationToken ct) =>
            {
                await attendance.SetDeviceActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateAttendanceDevice").RequirePermission(Permissions.Attendance.DeviceManage);
        group.MapPost("/devices/{id:guid}/deactivate", async (Guid id, AttendanceService attendance, CancellationToken ct) =>
            {
                await attendance.SetDeviceActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateAttendanceDevice").RequirePermission(Permissions.Attendance.DeviceManage);

        // Devices report through an integration account holding attendance.device.manage.
        group.MapPost("/punches/import", async (PunchImport body, AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.ImportAsync(body, ct)))
            .WithName("ImportAttendancePunches").RequirePermission(Permissions.Attendance.DeviceManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/attendance").WithTags("Attendance");
        me.MapGet("/today", async (AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.TodayAsync(ct)))
            .WithName("MyAttendanceToday").RequirePermission(Permissions.Self.AttendanceView);
        me.MapPost("/punch", async (AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.PunchAsync(ct)))
            .WithName("Punch").RequirePermission(Permissions.Self.AttendancePunch);
        me.MapGet("/", async (DateOnly from, DateOnly to, AttendanceService attendance, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(user.EmployeeId is { } self ? await attendance.DaysAsync([self], from, to, ct) : []))
            .WithName("MyAttendanceDays").RequirePermission(Permissions.Self.AttendanceView);
        me.MapGet("/regularizations", async (AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.RegularizationsAsync(mine: true, null, ct)))
            .WithName("MyRegularizations").RequirePermission(Permissions.Attendance.RegularizationCreate);
        me.MapPost("/regularizations", async (RegularizationBody body, AttendanceService attendance, CancellationToken ct) => TypedResults.Ok(await attendance.RequestRegularizationAsync(body, ct)))
            .WithName("RequestRegularization").RequirePermission(Permissions.Attendance.RegularizationCreate);
        me.MapPost("/regularizations/{id:guid}/cancel", async (Guid id, AttendanceService attendance, CancellationToken ct) =>
            {
                await attendance.CancelRegularizationAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelRegularization").RequirePermission(Permissions.Attendance.RegularizationCreate);

        return endpoints;
    }
}
