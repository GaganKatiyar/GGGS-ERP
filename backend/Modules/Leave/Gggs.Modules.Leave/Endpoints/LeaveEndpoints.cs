using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Application;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Endpoints;

public sealed record LeaveTypeItem(Guid Id, string Code, string Name, bool IsPaid, bool AllowHalfDay, string Color, bool IsSystem, bool IsActive);

public sealed record LeaveTypeRequest(string Code, string Name, bool IsPaid, bool AllowHalfDay, string? Color);

public sealed record LeavePolicyItem(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string? EmploymentTypeCode,
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    bool AllowDuringProbation,
    decimal MaxNegativeBalance,
    int? MaxConsecutiveDays,
    int MinNoticeDays,
    bool CountSandwichedDays,
    bool IsActive);

public sealed record LeavePolicyRequest(
    Guid LeaveTypeId,
    string? EmploymentTypeCode,
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    bool AllowDuringProbation,
    decimal MaxNegativeBalance,
    int? MaxConsecutiveDays,
    int MinNoticeDays,
    bool CountSandwichedDays)
{
    public PolicyRules Rules => new(AnnualEntitlement, Accrual, CarryForwardLimit, AllowDuringProbation, MaxNegativeBalance, MaxConsecutiveDays, MinNoticeDays, CountSandwichedDays);
}

public sealed record HolidayCalendarItem(Guid Id, string Code, string Name, bool IsDefault, IReadOnlyList<Guid> LocationIds, int HolidayCount);

public sealed record HolidayCalendarRequest(string Code, string Name, bool IsDefault, IReadOnlyList<Guid>? LocationIds);

public sealed record HolidayItem(Guid Id, Guid CalendarId, DateOnly Date, string Name, bool IsOptional);

public sealed record HolidayRequest(DateOnly Date, string Name, bool IsOptional);

public sealed record CancelLeaveRequest(string? Reason);

public sealed record AccrualRunRequest(int Year, int Month, Guid? EmployeeId);

/// <summary>Leave (spec §20): configuration, requests, balances and the ledger; self-service under <c>/api/v1/me</c>.</summary>
internal static class LeaveEndpoints
{
    public static IEndpointRouteBuilder MapLeaveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/leave").WithTags("Leave");

        // Types and policies.
        group.MapGet("/types", ListTypesAsync).WithName("ListLeaveTypes")
            .RequirePermission(Permissions.Leave.TypeManage, Permissions.Leave.PolicyManage, Permissions.Leave.RequestView, Permissions.Self.LeaveView);
        group.MapPost("/types", CreateTypeAsync).WithName("CreateLeaveType").RequirePermission(Permissions.Leave.TypeManage);
        group.MapPut("/types/{id:guid}", UpdateTypeAsync).WithName("UpdateLeaveType").RequirePermission(Permissions.Leave.TypeManage);
        group.MapPost("/types/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => SetTypeActiveAsync(id, true, db, ct))
            .WithName("ActivateLeaveType").RequirePermission(Permissions.Leave.TypeManage);
        group.MapPost("/types/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => SetTypeActiveAsync(id, false, db, ct))
            .WithName("DeactivateLeaveType").RequirePermission(Permissions.Leave.TypeManage);

        group.MapGet("/policies", ListPoliciesAsync).WithName("ListLeavePolicies").RequirePermission(Permissions.Leave.PolicyManage);
        group.MapPost("/policies", CreatePolicyAsync).WithName("CreateLeavePolicy").RequirePermission(Permissions.Leave.PolicyManage);
        group.MapPut("/policies/{id:guid}", UpdatePolicyAsync).WithName("UpdateLeavePolicy").RequirePermission(Permissions.Leave.PolicyManage);
        group.MapPost("/policies/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => SetPolicyActiveAsync(id, true, db, ct))
            .WithName("ActivateLeavePolicy").RequirePermission(Permissions.Leave.PolicyManage);
        group.MapPost("/policies/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => SetPolicyActiveAsync(id, false, db, ct))
            .WithName("DeactivateLeavePolicy").RequirePermission(Permissions.Leave.PolicyManage);
        group.MapPost("/accrual/run", async (AccrualRunRequest body, LeaveAccrualService accrual, CancellationToken ct) =>
                TypedResults.Ok(await accrual.RunAsync(body.Year, body.Month, ct, body.EmployeeId)))
            .WithName("RunLeaveAccrual").RequirePermission(Permissions.Leave.PolicyManage);

        // Holidays.
        group.MapGet("/holiday-calendars", ListCalendarsAsync).WithName("ListHolidayCalendars").RequirePermission(Permissions.Leave.HolidayManage);
        group.MapPost("/holiday-calendars", CreateCalendarAsync).WithName("CreateHolidayCalendar").RequirePermission(Permissions.Leave.HolidayManage);
        group.MapPut("/holiday-calendars/{id:guid}", UpdateCalendarAsync).WithName("UpdateHolidayCalendar").RequirePermission(Permissions.Leave.HolidayManage);
        group.MapGet("/holiday-calendars/{id:guid}/holidays", ListHolidaysAsync).WithName("ListHolidays").RequirePermission(Permissions.Leave.HolidayManage);
        group.MapPost("/holiday-calendars/{id:guid}/holidays", AddHolidayAsync).WithName("AddHoliday").RequirePermission(Permissions.Leave.HolidayManage);
        group.MapDelete("/holiday-calendars/{id:guid}/holidays/{holidayId:guid}", RemoveHolidayAsync).WithName("RemoveHoliday").RequirePermission(Permissions.Leave.HolidayManage);

        // Requests and balances.
        group.MapGet("/requests", async ([AsParameters] LeaveRequestQuery query, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.ListAsync(query, mine: false, ct)))
            .WithName("ListLeaveRequests").RequirePermission(Permissions.Leave.RequestView);
        group.MapPost("/requests", async (LeaveApplication body, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.ApplyAsync(body, onBehalf: true, ct)))
            .WithName("ApplyLeaveOnBehalf").RequirePermission(Permissions.Leave.RequestCreateOnBehalf);
        group.MapPost("/requests/preview", async (LeaveApplication body, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.PreviewAsync(body, onBehalf: true, ct)))
            .WithName("PreviewLeaveOnBehalf").RequirePermission(Permissions.Leave.RequestCreateOnBehalf);
        group.MapPost("/requests/{id:guid}/cancel", async (Guid id, CancelLeaveRequest? body, LeaveService leave, CancellationToken ct) =>
            {
                await leave.CancelAsync(id, body?.Reason, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelLeaveRequest").RequirePermission(Permissions.Leave.RequestCancel);
        group.MapGet("/calendar", async (DateOnly from, DateOnly to, Guid? departmentId, LeaveService leave, CancellationToken ct) =>
                TypedResults.Ok(await leave.CalendarAsync(from, to, departmentId, ct)))
            .WithName("LeaveCalendar").RequirePermission(Permissions.Leave.RequestView);

        group.MapGet("/balances", async (Guid employeeId, LeaveService leave, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                await RequireScopeAsync(employees, employeeId, Permissions.Leave.BalanceView, ct);
                return TypedResults.Ok(await leave.BalancesAsync(employeeId, ct));
            })
            .WithName("LeaveBalances").RequirePermission(Permissions.Leave.BalanceView);
        group.MapGet("/ledger", async (Guid employeeId, Guid? leaveTypeId, LeaveService leave, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                await RequireScopeAsync(employees, employeeId, Permissions.Leave.BalanceView, ct);
                return TypedResults.Ok(await leave.LedgerAsync(employeeId, leaveTypeId, ct));
            })
            .WithName("LeaveLedger").RequirePermission(Permissions.Leave.BalanceView);
        group.MapPost("/adjustments", async (AdjustmentRequest body, LeaveService leave, CancellationToken ct) =>
            {
                await leave.AdjustAsync(body, ct);
                return TypedResults.NoContent();
            })
            .WithName("AdjustLeaveBalance").RequirePermission(Permissions.Leave.BalanceAdjust);

        // Self-service.
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Leave");
        me.MapGet("/leave/balances", async (LeaveService leave, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(user.EmployeeId is { } self ? await leave.BalancesAsync(self, ct) : []))
            .WithName("MyLeaveBalances").RequirePermission(Permissions.Self.LeaveView);
        me.MapGet("/leave/requests", async ([AsParameters] LeaveRequestQuery query, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.ListAsync(query, mine: true, ct)))
            .WithName("MyLeaveRequests").RequirePermission(Permissions.Self.LeaveView);
        me.MapPost("/leave/requests", async (LeaveApplication body, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.ApplyAsync(body with { EmployeeId = null }, onBehalf: false, ct)))
            .WithName("ApplyMyLeave").RequirePermission(Permissions.Self.LeaveApply);
        me.MapPost("/leave/requests/preview", async (LeaveApplication body, LeaveService leave, CancellationToken ct) => TypedResults.Ok(await leave.PreviewAsync(body with { EmployeeId = null }, onBehalf: false, ct)))
            .WithName("PreviewMyLeave").RequirePermission(Permissions.Self.LeaveApply);
        me.MapPost("/leave/requests/{id:guid}/cancel", async (Guid id, CancelLeaveRequest? body, LeaveService leave, CancellationToken ct) =>
            {
                await leave.CancelAsync(id, body?.Reason, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelMyLeave").RequirePermission(Permissions.Self.LeaveApply);
        me.MapGet("/holidays", async (int? year, ILeaveCalendar calendar, ICurrentUser user, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } self)
                {
                    return TypedResults.Ok<IReadOnlyList<HolidayEntry>>([]);
                }

                int chosen = year ?? DateTime.UtcNow.Year;
                IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = await calendar.GetHolidaysAsync([self], new DateOnly(chosen, 1, 1), new DateOnly(chosen, 12, 31), ct);
                return TypedResults.Ok(holidays.GetValueOrDefault(self) ?? []);
            })
            .WithName("MyHolidays").RequirePermission(Permissions.Self.LeaveView);

        return endpoints;
    }

    private static async Task RequireScopeAsync(IEmployeeDirectory employees, Guid employeeId, string permission, CancellationToken ct)
    {
        if (!await employees.IsInScopeAsync(employeeId, permission, ct))
        {
            throw new NotFoundException("Employee", employeeId);
        }
    }

    // ----- Types -------------------------------------------------------------------------------

    private static async Task<Ok<List<LeaveTypeItem>>> ListTypesAsync(bool? includeInactive, AppDbContext database, CancellationToken ct)
        => TypedResults.Ok(await database.LeaveTypes.AsNoTracking()
            .Where(type => includeInactive == true || type.IsActive)
            .OrderBy(type => type.Name)
            .Select(type => new LeaveTypeItem(type.Id, type.Code, type.Name, type.IsPaid, type.AllowHalfDay, type.Color, type.IsSystem, type.IsActive))
            .ToListAsync(ct));

    private static async Task<Created<LeaveTypeItem>> CreateTypeAsync(LeaveTypeRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Code.Trim().ToUpperInvariant();
        if (await database.LeaveTypes.AnyAsync(type => type.Code == code, ct))
        {
            throw new DomainRuleException("LEAVE_TYPE_CODE_IN_USE", $"A leave type with code '{code}' already exists.");
        }

        var type = LeaveType.Create(code, body.Name, body.IsPaid, body.AllowHalfDay, body.Color);
        database.LeaveTypes.Add(type);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/leave/types/{type.Id}", ToItem(type));
    }

    private static async Task<Ok<LeaveTypeItem>> UpdateTypeAsync(Guid id, LeaveTypeRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        LeaveType type = await database.LeaveTypes.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(LeaveType), id);
        type.Update(body.Name, body.IsPaid, body.AllowHalfDay, body.Color);
        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(ToItem(type));
    }

    private static async Task<Ok<LeaveTypeItem>> SetTypeActiveAsync(Guid id, bool active, AppDbContext database, CancellationToken ct)
    {
        LeaveType type = await database.LeaveTypes.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(LeaveType), id);
        if (active)
        {
            type.Activate();
        }
        else
        {
            type.Deactivate();
        }

        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(ToItem(type));
    }

    // ----- Policies ----------------------------------------------------------------------------

    private static async Task<Ok<List<LeavePolicyItem>>> ListPoliciesAsync(AppDbContext database, CancellationToken ct)
        => TypedResults.Ok(await database.LeavePolicies.AsNoTracking()
            .Join(database.LeaveTypes.AsNoTracking(), policy => policy.LeaveTypeId, type => type.Id, (policy, type) => new { policy, type.Name })
            .OrderBy(row => row.Name).ThenBy(row => row.policy.EmploymentTypeCode)
            .Select(row => ToItem(row.policy, row.Name))
            .ToListAsync(ct));

    private static async Task<Created<LeavePolicyItem>> CreatePolicyAsync(LeavePolicyRequest body, AppDbContext database, IMasterDataLookup lookup, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        LeaveType type = await database.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.LeaveTypeId, ct)
            ?? throw new NotFoundException(nameof(LeaveType), body.LeaveTypeId);
        await EnsureNoClashAsync(database, lookup, body.LeaveTypeId, body.EmploymentTypeCode, null, ct);

        var policy = LeavePolicy.Create(type.Id, body.EmploymentTypeCode, body.Rules);
        database.LeavePolicies.Add(policy);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/leave/policies/{policy.Id}", ToItem(policy, type.Name));
    }

    private static async Task<Ok<LeavePolicyItem>> UpdatePolicyAsync(Guid id, LeavePolicyRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        LeavePolicy policy = await database.LeavePolicies.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(LeavePolicy), id);
        policy.Update(body.Rules);
        await database.SaveChangesAsync(ct);
        string typeName = await database.LeaveTypes.Where(type => type.Id == policy.LeaveTypeId).Select(type => type.Name).FirstAsync(ct);
        return TypedResults.Ok(ToItem(policy, typeName));
    }

    private static async Task<NoContent> SetPolicyActiveAsync(Guid id, bool active, AppDbContext database, CancellationToken ct)
    {
        LeavePolicy policy = await database.LeavePolicies.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(LeavePolicy), id);
        if (active)
        {
            if (await database.LeavePolicies.AnyAsync(
                    other => other.Id != id && other.IsActive && other.LeaveTypeId == policy.LeaveTypeId && other.EmploymentTypeCode == policy.EmploymentTypeCode, ct))
            {
                throw new DomainRuleException("LEAVE_POLICY_CLASH", "Another active policy already covers this leave type and employment type.");
            }

            policy.Activate();
        }
        else
        {
            policy.Deactivate();
        }

        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task EnsureNoClashAsync(AppDbContext database, IMasterDataLookup lookup, Guid typeId, string? employmentTypeCode, Guid? exceptId, CancellationToken ct)
    {
        string? code = string.IsNullOrWhiteSpace(employmentTypeCode) ? null : employmentTypeCode.Trim().ToUpperInvariant();
        if (code is not null && !await lookup.IsValidAsync("employment-type", code, ct))
        {
            throw new DomainRuleException("LEAVE_POLICY_EMPLOYMENT_TYPE", $"'{code}' is not an employment type.");
        }

        if (await database.LeavePolicies.AnyAsync(
                policy => policy.Id != exceptId && policy.IsActive && policy.LeaveTypeId == typeId && policy.EmploymentTypeCode == code, ct))
        {
            throw new DomainRuleException("LEAVE_POLICY_CLASH", "An active policy already covers this leave type and employment type.");
        }
    }

    // ----- Holidays ----------------------------------------------------------------------------

    private static async Task<Ok<List<HolidayCalendarItem>>> ListCalendarsAsync(AppDbContext database, CancellationToken ct)
    {
        List<HolidayCalendar> calendars = await database.HolidayCalendars.AsNoTracking().OrderByDescending(calendar => calendar.IsDefault).ThenBy(calendar => calendar.Name).ToListAsync(ct);
        Dictionary<Guid, int> counts = await database.Holidays.AsNoTracking()
            .GroupBy(holiday => holiday.CalendarId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct);

        return TypedResults.Ok(calendars.Select(calendar => new HolidayCalendarItem(
            calendar.Id, calendar.Code, calendar.Name, calendar.IsDefault, calendar.LocationIds, counts.GetValueOrDefault(calendar.Id))).ToList());
    }

    private static async Task<Created<HolidayCalendarItem>> CreateCalendarAsync(HolidayCalendarRequest body, AppDbContext database, IOrgStructure org, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Code.Trim().ToUpperInvariant();
        if (await database.HolidayCalendars.AnyAsync(calendar => calendar.Code == code, ct))
        {
            throw new DomainRuleException("HOLIDAY_CALENDAR_CODE_IN_USE", $"A calendar with code '{code}' already exists.");
        }

        List<Guid> locations = await ValidLocationsAsync(org, body.LocationIds, ct);
        if (body.IsDefault)
        {
            await ClearDefaultAsync(database, null, ct);
        }

        var calendar = HolidayCalendar.Create(code, body.Name, body.IsDefault, locations);
        database.HolidayCalendars.Add(calendar);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/leave/holiday-calendars/{calendar.Id}", new HolidayCalendarItem(calendar.Id, calendar.Code, calendar.Name, calendar.IsDefault, calendar.LocationIds, 0));
    }

    private static async Task<Ok<HolidayCalendarItem>> UpdateCalendarAsync(Guid id, HolidayCalendarRequest body, AppDbContext database, IOrgStructure org, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        HolidayCalendar calendar = await database.HolidayCalendars.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(HolidayCalendar), id);
        List<Guid> locations = await ValidLocationsAsync(org, body.LocationIds, ct);
        if (body.IsDefault)
        {
            await ClearDefaultAsync(database, id, ct);
        }

        calendar.Update(body.Name, body.IsDefault, locations);
        await database.SaveChangesAsync(ct);
        int count = await database.Holidays.CountAsync(holiday => holiday.CalendarId == id, ct);
        return TypedResults.Ok(new HolidayCalendarItem(calendar.Id, calendar.Code, calendar.Name, calendar.IsDefault, calendar.LocationIds, count));
    }

    private static async Task<Ok<List<HolidayItem>>> ListHolidaysAsync(Guid id, int? year, AppDbContext database, CancellationToken ct)
    {
        int chosen = year ?? DateTime.UtcNow.Year;
        var first = new DateOnly(chosen, 1, 1);
        var last = new DateOnly(chosen, 12, 31);
        return TypedResults.Ok(await database.Holidays.AsNoTracking()
            .Where(holiday => holiday.CalendarId == id && holiday.Date >= first && holiday.Date <= last)
            .OrderBy(holiday => holiday.Date)
            .Select(holiday => new HolidayItem(holiday.Id, holiday.CalendarId, holiday.Date, holiday.Name, holiday.IsOptional))
            .ToListAsync(ct));
    }

    private static async Task<Created<HolidayItem>> AddHolidayAsync(Guid id, HolidayRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await database.HolidayCalendars.AnyAsync(calendar => calendar.Id == id, ct))
        {
            throw new NotFoundException(nameof(HolidayCalendar), id);
        }

        if (await database.Holidays.AnyAsync(holiday => holiday.CalendarId == id && holiday.Date == body.Date, ct))
        {
            throw new DomainRuleException("HOLIDAY_EXISTS", "That calendar already has a holiday on this date.");
        }

        var holiday = Holiday.Create(id, body.Date, body.Name, body.IsOptional);
        database.Holidays.Add(holiday);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/leave/holiday-calendars/{id}/holidays/{holiday.Id}", new HolidayItem(holiday.Id, id, holiday.Date, holiday.Name, holiday.IsOptional));
    }

    private static async Task<NoContent> RemoveHolidayAsync(Guid id, Guid holidayId, AppDbContext database, CancellationToken ct)
    {
        Holiday holiday = await database.Holidays.FirstOrDefaultAsync(row => row.Id == holidayId && row.CalendarId == id, ct) ?? throw new NotFoundException(nameof(Holiday), holidayId);
        database.Holidays.Remove(holiday);
        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<Guid>> ValidLocationsAsync(IOrgStructure org, IReadOnlyList<Guid>? locationIds, CancellationToken ct)
    {
        List<Guid> locations = [.. (locationIds ?? []).Distinct()];
        IReadOnlyList<OrgReference> invalid = await org.FindInvalidAsync(locations.Select(id => new OrgReference(OrgUnitKind.Location, id)), ct);
        return invalid.Count == 0 ? locations : throw new DomainRuleException("HOLIDAY_CALENDAR_LOCATION", "A chosen location does not exist or is inactive.");
    }

    private static async Task ClearDefaultAsync(AppDbContext database, Guid? exceptId, CancellationToken ct)
    {
        foreach (HolidayCalendar calendar in await database.HolidayCalendars.Where(row => row.IsDefault && row.Id != exceptId).ToListAsync(ct))
        {
            calendar.Update(calendar.Name, isDefault: false, calendar.LocationIds);
        }
    }

    private static LeaveTypeItem ToItem(LeaveType type) => new(type.Id, type.Code, type.Name, type.IsPaid, type.AllowHalfDay, type.Color, type.IsSystem, type.IsActive);

    private static LeavePolicyItem ToItem(LeavePolicy policy, string typeName)
        => new(
            policy.Id, policy.LeaveTypeId, typeName, policy.EmploymentTypeCode, policy.AnnualEntitlement, policy.Accrual, policy.CarryForwardLimit,
            policy.AllowDuringProbation, policy.MaxNegativeBalance, policy.MaxConsecutiveDays, policy.MinNoticeDays, policy.CountSandwichedDays, policy.IsActive);
}
