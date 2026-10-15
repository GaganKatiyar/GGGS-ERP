using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Timesheets.Application;
using Gggs.Modules.Timesheets.Domain;

namespace Gggs.Modules.Timesheets.Endpoints;

/// <summary>Timesheets: one's own week under <c>/api/v1/me/timesheets</c>, everyone else's under <c>/api/v1/timesheets</c> (spec §70-71).</summary>
internal static class TimesheetEndpoints
{
    public static IEndpointRouteBuilder MapTimesheetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/timesheets").WithTags("Timesheets");
        me.MapGet("/", async (DateOnly? week, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.MyWeekAsync(week, ct)))
            .WithName("MyTimesheetWeek").RequirePermission(Permissions.Self.TimesheetSubmit);
        me.MapPut("/{week}", async (DateOnly week, WeekBody body, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.SaveMyWeekAsync(week, body, ct)))
            .WithName("SaveMyTimesheetWeek").RequirePermission(Permissions.Self.TimesheetSubmit);
        me.MapPost("/{week}/copy-previous", async (DateOnly week, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.CopyPreviousWeekAsync(week, ct)))
            .WithName("CopyPreviousTimesheetWeek").RequirePermission(Permissions.Self.TimesheetSubmit);
        me.MapPost("/{week}/submit", async (DateOnly week, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.SubmitMyWeekAsync(week, ct)))
            .WithName("SubmitMyTimesheetWeek").RequirePermission(Permissions.Self.TimesheetSubmit);
        me.MapPost("/{week}/withdraw", async (DateOnly week, TimesheetService timesheets, CancellationToken ct) =>
            {
                await timesheets.WithdrawMyWeekAsync(week, ct);
                return TypedResults.NoContent();
            })
            .WithName("WithdrawMyTimesheetWeek").RequirePermission(Permissions.Self.TimesheetSubmit);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/timesheets").WithTags("Timesheets");
        group.MapGet("/", async ([AsParameters] TimesheetListQuery query, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.ListAsync(query, ct)))
            .WithName("ListTimesheets").RequirePermission(Permissions.Timesheet.View);
        group.MapGet("/missing", async (DateOnly week, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.MissingAsync(week, ct)))
            .WithName("MissingTimesheets").RequirePermission(Permissions.Timesheet.View);
        group.MapGet("/projects/{projectId:guid}", async (Guid projectId, DateOnly from, DateOnly to, TimesheetService timesheets, CancellationToken ct) =>
                TypedResults.Ok(await timesheets.ProjectHoursAsync(projectId, from, to, ct)))
            .WithName("ProjectTimesheetHours").RequirePermission(Permissions.Project.View);
        group.MapGet("/{id:guid}", async (Guid id, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.GetAsync(id, ct)))
            .WithName("GetTimesheet").RequirePermission(Permissions.Timesheet.View, Permissions.Self.TimesheetSubmit, Permissions.Workflow.TaskAct);
        group.MapPost("/{id:guid}/reopen", async (Guid id, ReopenBody body, TimesheetService timesheets, CancellationToken ct) =>
            {
                await timesheets.ReopenAsync(id, body.Reason, ct);
                return TypedResults.NoContent();
            })
            .WithName("ReopenTimesheet").RequirePermission(Permissions.Timesheet.Approve);

        group.MapGet("/settings", async (TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.RulesAsync(ct)))
            .WithName("GetTimesheetRules").RequirePermission(Permissions.Timesheet.View, Permissions.Self.TimesheetSubmit);
        group.MapPut("/settings", async (TimesheetRules body, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.SaveRulesAsync(body, ct)))
            .WithName("SaveTimesheetRules").RequirePermission(Permissions.Timesheet.SettingsManage);
        group.MapPost("/settings/lock", async (LockBody body, TimesheetService timesheets, CancellationToken ct) => TypedResults.Ok(await timesheets.LockAsync(body.Through, ct)))
            .WithName("LockTimesheetPeriod").RequirePermission(Permissions.Timesheet.SettingsManage);

        return endpoints;
    }
}
