using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Shift.Application;
using Gggs.Modules.Shift.Contracts;

namespace Gggs.Modules.Shift.Endpoints;

public sealed record AssignedCount(int Assigned);

/// <summary>Shifts, rosters and swaps (spec §19), and each employee's own schedule under <c>/api/v1/me</c>.</summary>
internal static class ShiftEndpoints
{
    public static IEndpointRouteBuilder MapShiftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/shifts").WithTags("Shifts");

        group.MapGet("/", async (ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.ListAsync(ct)))
            .WithName("ListShifts").RequirePermission(Permissions.Attendance.ShiftManage, Permissions.Attendance.RosterManage, Permissions.Attendance.View);
        group.MapPost("/", async (ShiftRequest body, ShiftService shifts, CancellationToken ct) =>
            {
                ShiftItem created = await shifts.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/shifts/{created.Id}", created);
            })
            .WithName("CreateShift").RequirePermission(Permissions.Attendance.ShiftManage);
        group.MapPut("/{id:guid}", async (Guid id, ShiftRequest body, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.UpdateAsync(id, body, ct)))
            .WithName("UpdateShift").RequirePermission(Permissions.Attendance.ShiftManage);
        group.MapPost("/{id:guid}/default", async (Guid id, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.MakeDefaultAsync(id, ct)))
            .WithName("MakeShiftDefault").RequirePermission(Permissions.Attendance.ShiftManage);
        group.MapPost("/{id:guid}/activate", async (Guid id, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.SetActiveAsync(id, true, ct)))
            .WithName("ActivateShift").RequirePermission(Permissions.Attendance.ShiftManage);
        group.MapPost("/{id:guid}/deactivate", async (Guid id, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.SetActiveAsync(id, false, ct)))
            .WithName("DeactivateShift").RequirePermission(Permissions.Attendance.ShiftManage);

        group.MapGet("/assignments", async (Guid employeeId, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.AssignmentsAsync(employeeId, ct)))
            .WithName("ListShiftAssignments").RequirePermission(Permissions.Attendance.RosterManage, Permissions.Attendance.View);
        group.MapPost("/assignments", async (AssignShiftRequest body, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(new AssignedCount(await shifts.AssignAsync(body, ct))))
            .WithName("AssignShift").RequirePermission(Permissions.Attendance.RosterManage);

        group.MapGet("/roster", async (DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, ShiftService shifts, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                IReadOnlyList<Guid> people = employeeId is { } single
                    ? [single]
                    : await employees.ListActiveIdsAsync(departmentId is { } department ? [department] : null, null, ct);
                return TypedResults.Ok(await shifts.RosterAsync([.. people], from, to, ct));
            })
            .WithName("GetRoster").RequirePermission(Permissions.Attendance.RosterManage, Permissions.Attendance.View);
        group.MapPut("/roster/overrides", async (OverrideRequest body, ShiftService shifts, CancellationToken ct) =>
            {
                await shifts.SetOverrideAsync(body, ct);
                return TypedResults.NoContent();
            })
            .WithName("SetRosterOverride").RequirePermission(Permissions.Attendance.RosterManage);
        group.MapDelete("/roster/overrides", async (Guid employeeId, DateOnly date, ShiftService shifts, CancellationToken ct) =>
            {
                await shifts.ClearOverrideAsync(employeeId, date, ct);
                return TypedResults.NoContent();
            })
            .WithName("ClearRosterOverride").RequirePermission(Permissions.Attendance.RosterManage);

        group.MapGet("/swaps", async (ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.SwapsAsync(everyone: true, ct)))
            .WithName("ListShiftSwaps").RequirePermission(Permissions.Attendance.RosterManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Shifts");
        me.MapGet("/shifts", async (DateOnly from, DateOnly to, IShiftSchedule schedule, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(user.EmployeeId is { } self ? await schedule.GetAsync([self], from, to, ct) : []))
            .WithName("ListMyShifts").RequirePermission(Permissions.Self.AttendanceView);
        me.MapGet("/shift-swaps", async (ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.SwapsAsync(everyone: false, ct)))
            .WithName("ListMyShiftSwaps").RequirePermission(Permissions.Self.ShiftSwapRequest);
        me.MapPost("/shift-swaps", async (SwapRequestBody body, ShiftService shifts, CancellationToken ct) => TypedResults.Ok(await shifts.RequestSwapAsync(body, ct)))
            .WithName("RequestShiftSwap").RequirePermission(Permissions.Self.ShiftSwapRequest);
        me.MapPost("/shift-swaps/{id:guid}/cancel", async (Guid id, ShiftService shifts, CancellationToken ct) =>
            {
                await shifts.CancelSwapAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelShiftSwap").RequirePermission(Permissions.Self.ShiftSwapRequest);

        return endpoints;
    }
}
