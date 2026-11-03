using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Modules.Leave.Endpoints;

/// <summary>
/// Leave policies under <c>/api/v1/leave</c>: the named policies and their rules, which engagement
/// type follows which, and per-employee exceptions; plus "which policy applies" for HR and for the
/// employee themself.
/// </summary>
internal static class LeavePlanEndpoints
{
    public static void Map(RouteGroupBuilder group, RouteGroupBuilder me)
    {
        group.MapGet("/plans", async (LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.ListAsync(ct)))
            .WithName("ListLeavePlans").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapGet("/plans/{id:guid}", async (Guid id, LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.GetAsync(id, ct)))
            .WithName("GetLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/plans", async (LeavePlanRequest body, LeavePlanService plans, CancellationToken ct) =>
            {
                LeavePlanDetail plan = await plans.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/leave/plans/{plan.Id}", plan);
            })
            .WithName("CreateLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPut("/plans/{id:guid}", async (Guid id, LeavePlanRequest body, LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.UpdateAsync(id, body, ct)))
            .WithName("UpdateLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPut("/plans/{id:guid}/items", async (Guid id, IReadOnlyList<LeavePlanItemRequest> body, LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.SetItemsAsync(id, body, ct)))
            .WithName("SetLeavePlanItems").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/plans/{id:guid}/copy", async (Guid id, LeavePlanService plans, CancellationToken ct) =>
            {
                LeavePlanDetail copy = await plans.CopyAsync(id, ct);
                return TypedResults.Created($"/api/v1/leave/plans/{copy.Id}", copy);
            })
            .WithName("CopyLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/plans/{id:guid}/activate", async (Guid id, LeavePlanService plans, CancellationToken ct) =>
            {
                await plans.SetActiveAsync(id, active: true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/plans/{id:guid}/deactivate", async (Guid id, LeavePlanService plans, CancellationToken ct) =>
            {
                await plans.SetActiveAsync(id, active: false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapGet("/plans/resolve", async Task<Results<Ok<ResolvedLeavePolicy>, NoContent>> (Guid employeeId, DateOnly? on, LeavePlanService plans, IEmployeeDirectory employees, ITenantClock clock, CancellationToken ct) =>
            {
                if (!await employees.IsInScopeAsync(employeeId, Permissions.Leave.BalanceView, ct))
                {
                    throw new NotFoundException("Employee", employeeId);
                }

                ResolvedLeavePolicy? policy = await plans.ResolveAsync(employeeId, on ?? await clock.TodayAsync(ct), ct);
                return policy is null ? TypedResults.NoContent() : TypedResults.Ok(policy);
            })
            .WithName("ResolveLeavePlan").RequirePermission(Permissions.Leave.BalanceView);

        group.MapGet("/plan-assignments", async (LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.AssignmentsAsync(ct)))
            .WithName("ListLeavePlanAssignments").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPut("/plan-assignments", async (IReadOnlyList<PlanAssignmentChange> body, LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.SetAssignmentsAsync(body, ct)))
            .WithName("SetLeavePlanAssignments").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapGet("/employee-plans", async (Guid? employeeId, LeavePlanService plans, CancellationToken ct) => TypedResults.Ok(await plans.ExceptionsAsync(employeeId, ct)))
            .WithName("ListEmployeeLeavePlans").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/employee-plans", async (EmployeePlanRequest body, LeavePlanService plans, CancellationToken ct) =>
            {
                Guid id = await plans.AddExceptionAsync(body, ct);
                return TypedResults.Created($"/api/v1/leave/employee-plans/{id}", new { id });
            })
            .WithName("AddEmployeeLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPut("/employee-plans/{id:guid}", async (Guid id, EmployeePlanRequest body, LeavePlanService plans, CancellationToken ct) =>
            {
                await plans.UpdateExceptionAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapDelete("/employee-plans/{id:guid}", async (Guid id, LeavePlanService plans, CancellationToken ct) =>
            {
                await plans.RemoveExceptionAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveEmployeeLeavePlan").RequirePermission(Permissions.Leave.PolicyManage);

        me.MapGet("/leave/policy", async Task<Results<Ok<ResolvedLeavePolicy>, NoContent, NotFound>> (LeavePlanService plans, ICurrentUser user, ITenantClock clock, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } self)
                {
                    return TypedResults.NotFound();
                }

                ResolvedLeavePolicy? policy = await plans.ResolveAsync(self, await clock.TodayAsync(ct), ct);
                return policy is null ? TypedResults.NoContent() : TypedResults.Ok(policy);
            })
            .WithName("MyLeavePolicy").RequirePermission(Permissions.Self.LeaveView);
    }
}
