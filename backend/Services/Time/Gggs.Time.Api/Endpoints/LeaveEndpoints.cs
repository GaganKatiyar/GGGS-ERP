using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Application.Leave;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Api.Endpoints;

public static class LeaveEndpoints
{
    public static IEndpointRouteBuilder MapLeaveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapLeaveTypeEndpoints(endpoints);
        MapLeavePolicyEndpoints(endpoints);
        MapLeaveBalanceEndpoints(endpoints);
        MapLeaveRequestEndpoints(endpoints);

        return endpoints;
    }

    private static void MapLeaveTypeEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/leaves/types").WithTags("Leave");

        group.MapGet("/", async (TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<LeaveTypeResponse> items = await database.LeaveTypes.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => ToResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListLeaveTypes").RequirePermission(Permissions.Leave.RequestView);

        group.MapPost("/", async (CreateLeaveTypeBody body, LeaveTypeService service, CancellationToken cancellationToken) =>
        {
            LeaveType entity = await service.CreateAsync(
                body.Code, body.Name, body.IsPaid, body.Unit, body.GenderRestrictionCode,
                body.IsCompOff, body.IsLop, body.ColorHex, cancellationToken);

            return TypedResults.Created($"/api/v1/leaves/types/{entity.Id}", ToResponse(entity));
        }).WithName("CreateLeaveType").RequirePermission(Permissions.Leave.TypeManage);
    }

    private static void MapLeavePolicyEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/leaves/policies").WithTags("Leave");

        group.MapGet("/", async (TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<LeavePolicyResponse> items = await database.LeavePolicies.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new LeavePolicyResponse(x.Id, x.Name, x.Version))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListLeavePolicies").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/", async (CreateLeavePolicyBody body, LeavePolicyService service, CancellationToken cancellationToken) =>
        {
            LeavePolicy entity = await service.CreateAsync(body.Name, cancellationToken);
            return TypedResults.Created(
                $"/api/v1/leaves/policies/{entity.Id}", new LeavePolicyResponse(entity.Id, entity.Name, entity.Version));
        }).WithName("CreateLeavePolicy").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapGet("/{id:guid}/rules", async (Guid id, TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<LeavePolicyRuleResponse> items = await database.LeavePolicyRules.AsNoTracking()
                .Where(x => x.PolicyId == id)
                .Select(x => ToRuleResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListLeavePolicyRules").RequirePermission(Permissions.Leave.PolicyManage);

        group.MapPost("/{id:guid}/rules", async (
            Guid id, CreateLeavePolicyRuleBody body, LeavePolicyService service, CancellationToken cancellationToken) =>
        {
            LeavePolicyRule entity = await service.CreateRuleAsync(
                id, body.LeaveTypeId, body.AccrualFrequency, body.AccrualAmount, body.ProRataOnJoining,
                body.MaxBalance, body.CarryForwardMax, body.CarryForwardExpiryMonths, body.EncashmentAllowed,
                body.EligibleAfterDays, body.MinNoticeDays, body.MaxConsecutiveDays, body.MinDaysPerRequest,
                body.AllowHalfDay, body.AllowNegativeBalance, body.NegativeLimit, cancellationToken);

            return TypedResults.Created($"/api/v1/leaves/policies/{id}/rules/{entity.Id}", ToRuleResponse(entity));
        }).WithName("CreateLeavePolicyRule").RequirePermission(Permissions.Leave.PolicyManage);
    }

    private static void MapLeaveBalanceEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/leaves/balances").WithTags("Leave");

        group.MapGet("/me", async (
            [FromQuery] int? year, TimeDbContext database, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            if (currentUser.EmployeeId is not { } employeeId)
            {
                return Results.Ok(Array.Empty<LeaveBalanceResponse>());
            }

            int periodYear = year ?? clock.GetUtcNow().Year;

            List<LeaveBalanceResponse> items = await database.LeaveBalances.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.PeriodYear == periodYear)
                .Select(x => ToBalanceResponse(x))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        }).WithName("GetMyLeaveBalances").RequirePermission(Permissions.Self.LeaveView);

        group.MapGet("/{employeeId:guid}", async (
            Guid employeeId, [FromQuery] int? year, TimeDbContext database, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            int periodYear = year ?? clock.GetUtcNow().Year;

            List<LeaveBalanceResponse> items = await database.LeaveBalances.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.PeriodYear == periodYear)
                .Select(x => ToBalanceResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("GetEmployeeLeaveBalances").RequirePermission(Permissions.Leave.BalanceView);
    }

    private static void MapLeaveRequestEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/leaves/requests").WithTags("Leave");

        group.MapGet("/", async (
            [AsParameters] PageQuery page,
            [FromQuery] Guid? employeeId,
            [FromQuery] LeaveRequestStatus? status,
            TimeDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            PageRequest request = page.ToPageRequest();

            // No employeeId filter and no leave.request.view-all-held caller: default to the
            // caller's own requests, the same self-scoping InboxEndpoints uses for notifications -
            // there is no route or query parameter another employee's id could be substituted into
            // that this does not already resolve safely.
            Guid? scopedEmployeeId = employeeId ?? currentUser.EmployeeId;

            IQueryable<LeaveRequest> query = database.LeaveRequests.AsNoTracking();

            if (scopedEmployeeId is { } id)
            {
                query = query.Where(x => x.EmployeeId == id);
            }

            if (status is { } statusFilter)
            {
                query = query.Where(x => x.Status == statusFilter);
            }

            long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

            List<LeaveRequestListItem> items = await query
                .OrderByDescending(x => x.FromDate)
                .Skip(request.Skip)
                .Take(request.PageSize)
                .Select(x => new LeaveRequestListItem(x.Id, x.RequestNo, x.EmployeeId, x.LeaveTypeId, x.FromDate, x.ToDate, x.TotalDays, x.Status))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(PagedResult.From(items, request, total));
        }).WithName("ListLeaveRequests").RequirePermission(Permissions.Leave.RequestView);

        group.MapGet("/{id:guid}", async (Guid id, TimeDbContext database, CancellationToken cancellationToken) =>
        {
            LeaveRequest? entity = await database.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetLeaveRequest").RequirePermission(Permissions.Leave.RequestView);

        group.MapPost("/", async (
            CreateLeaveRequestBody body,
            LeaveRequestService service,
            ICurrentUser currentUser,
            IPermissionProvider permissions,
            CancellationToken cancellationToken) =>
        {
            if (body.EmployeeId is { } requestedEmployeeId && requestedEmployeeId != currentUser.EmployeeId)
            {
                PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

                if (!held.Has(Permissions.Leave.RequestCreateOnBehalf))
                {
                    return Results.Forbid();
                }
            }

            LeaveRequest request = await service.SubmitAsync(
                body.EmployeeId, body.LeaveTypeId, body.FromDate, body.FromSession, body.ToDate, body.ToSession,
                body.Reason, body.AttachmentDocumentId, cancellationToken);

            return Results.Created($"/api/v1/leaves/requests/{request.Id}", ToResponse(request));
        }).WithName("CreateLeaveRequest").RequirePermission(Permissions.Leave.RequestCreate);

        group.MapPost("/{id:guid}/cancel", async (Guid id, LeaveRequestService service, CancellationToken cancellationToken) =>
        {
            await service.CancelAsync(id, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("CancelLeaveRequest").RequirePermission(Permissions.Leave.RequestCancel);
    }

    private static LeaveTypeResponse ToResponse(LeaveType entity) => new(
        entity.Id, entity.Code, entity.Name, entity.IsPaid, entity.Unit, entity.GenderRestrictionCode,
        entity.IsCompOff, entity.IsLop, entity.ColorHex);

    private static LeavePolicyRuleResponse ToRuleResponse(LeavePolicyRule entity) => new(
        entity.Id, entity.PolicyId, entity.LeaveTypeId, entity.AccrualFrequency, entity.AccrualAmount,
        entity.ProRataOnJoining, entity.MaxBalance, entity.CarryForwardMax, entity.CarryForwardExpiryMonths,
        entity.EncashmentAllowed, entity.EligibleAfterDays, entity.MinNoticeDays, entity.MaxConsecutiveDays,
        entity.MinDaysPerRequest, entity.AllowHalfDay, entity.AllowNegativeBalance, entity.NegativeLimit);

    private static LeaveBalanceResponse ToBalanceResponse(LeaveBalance entity) => new(
        entity.LeaveTypeId, entity.PeriodYear, entity.Opening, entity.Accrued, entity.Taken, entity.PendingApproval,
        entity.Adjusted, entity.Encashed, entity.Lapsed, entity.CarriedForward, entity.Available);

    private static LeaveRequestResponse ToResponse(LeaveRequest entity) => new(
        entity.Id, entity.RequestNo, entity.EmployeeId, entity.LeaveTypeId, entity.FromDate, entity.FromSession,
        entity.ToDate, entity.ToSession, entity.TotalDays, entity.Reason, entity.Status, entity.WorkflowInstanceId,
        entity.CreatedDate);
}
