using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Shift.Domain;
using Gggs.Modules.Shift.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Shift.Application;

public sealed record ShiftItem(
    Guid Id,
    string Code,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int GraceMinutes,
    int HalfDayMinutes,
    int FullDayMinutes,
    IReadOnlyList<DayOfWeek> WeeklyOff,
    bool IsDefault,
    bool IsActive);

public sealed record ShiftRequest(
    string Code,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int GraceMinutes,
    int HalfDayMinutes,
    int FullDayMinutes,
    IReadOnlyList<DayOfWeek> WeeklyOff);

public sealed record AssignmentItem(Guid Id, Guid EmployeeId, string? EmployeeName, Guid ShiftId, string ShiftName, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record AssignShiftRequest(IReadOnlyList<Guid> EmployeeIds, Guid ShiftId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record OverrideRequest(Guid EmployeeId, DateOnly Date, Guid? ShiftId, string? Reason);

public sealed record RosterRow(Guid EmployeeId, string EmployeeName, string EmployeeCode, IReadOnlyList<ScheduledDay> Days);

public sealed record SwapItem(
    Guid Id,
    Guid RequesterEmployeeId,
    string? RequesterName,
    Guid CounterpartEmployeeId,
    string? CounterpartName,
    DateOnly Date,
    string? Reason,
    SwapStatus Status,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate);

public sealed record SwapRequestBody(Guid CounterpartEmployeeId, DateOnly Date, string? Reason);

/// <summary>
/// Shifts and rosters (spec §19): the patterns, who works which and when, one-day changes, and
/// swaps between colleagues - approved through Workflow when the tenant has a definition for them.
/// </summary>
internal sealed class ShiftService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IWorkflowStarter workflow,
    ICurrentUser currentUser,
    ITenantClock clock) : IShiftSchedule
{
    public const string SwapSubjectType = "shift.swap";

    public static WorkflowSubject SwapSubject { get; } = new(SwapSubjectType, "Swap a shift", "shift", []);

    /// <summary>Used when a tenant has defined no shift at all: a plain office day, weekends off.</summary>
    private static readonly WorkShift Standard = WorkShift.Create(
        "STANDARD", "Standard day", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 15, 240, 480, [DayOfWeek.Saturday, DayOfWeek.Sunday]);

    // ----- Schedule ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<ScheduledDay>> GetAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        DateOnly from = firstDay;
        DateOnly to = lastDay;
        if (to < from || to.DayNumber - from.DayNumber > 400)
        {
            throw new DomainRuleException("SHIFT_RANGE", "Ask for at most 400 days, ending after they start.");
        }

        List<Guid> ids = [.. employeeIds.Distinct()];
        if (ids.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, WorkShift> shifts = await dbContext.Shifts.AsNoTracking().ToDictionaryAsync(shift => shift.Id, cancellationToken);
        WorkShift fallback = shifts.Values.FirstOrDefault(shift => shift.IsDefault && shift.IsActive) ?? Standard;

        List<ShiftAssignment> assignments = await dbContext.ShiftAssignments.AsNoTracking()
            .Where(assignment => ids.Contains(assignment.EmployeeId)
                && assignment.EffectiveFrom <= to
                && (assignment.EffectiveTo == null || assignment.EffectiveTo >= from))
            .ToListAsync(cancellationToken);

        Dictionary<(Guid, DateOnly), RosterOverride> overrides = (await dbContext.RosterOverrides.AsNoTracking()
                .Where(entry => ids.Contains(entry.EmployeeId) && entry.Date >= from && entry.Date <= to)
                .ToListAsync(cancellationToken))
            .ToDictionary(entry => (entry.EmployeeId, entry.Date));

        var days = new List<ScheduledDay>(ids.Count * (to.DayNumber - from.DayNumber + 1));
        foreach (Guid employeeId in ids)
        {
            List<ShiftAssignment> mine = [.. assignments.Where(assignment => assignment.EmployeeId == employeeId)];
            for (DateOnly day = from; day <= to; day = day.AddDays(1))
            {
                if (overrides.TryGetValue((employeeId, day), out RosterOverride? entry))
                {
                    WorkShift? chosen = entry.ShiftId is { } id ? shifts.GetValueOrDefault(id) : null;
                    days.Add(chosen is null ? DayOff(employeeId, day, fallback) : Scheduled(employeeId, day, chosen, weeklyOff: false));
                    continue;
                }

                ShiftAssignment? assignment = mine.FirstOrDefault(candidate => candidate.Covers(day));
                WorkShift shift = assignment is not null ? shifts.GetValueOrDefault(assignment.ShiftId) ?? fallback : fallback;
                days.Add(Scheduled(employeeId, day, shift, shift.IsOff(day.DayOfWeek)));
            }
        }

        return days;
    }

    public async Task<IReadOnlyList<RosterRow>> RosterAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to.DayNumber - from.DayNumber > 62)
        {
            throw new DomainRuleException("SHIFT_ROSTER_RANGE", "A roster shows at most two months at a time.");
        }

        IReadOnlyList<EmployeeSummary> people = await employees.GetManyAsync(employeeIds.Take(300), cancellationToken);
        IReadOnlyList<ScheduledDay> days = await GetAsync([.. people.Select(person => person.Id)], from, to, cancellationToken);

        return [.. people
            .OrderBy(person => person.DisplayName, StringComparer.CurrentCulture)
            .Select(person => new RosterRow(person.Id, person.DisplayName, person.EmployeeCode, [.. days.Where(day => day.EmployeeId == person.Id)]))];
    }

    // ----- Shifts ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ShiftItem>> ListAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.Shifts.AsNoTracking().OrderBy(shift => shift.StartTime).ThenBy(shift => shift.Name).ToListAsync(cancellationToken)).Select(ToItem)];

    public async Task<ShiftItem> CreateAsync(ShiftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string code = request.Code.Trim().ToUpperInvariant();
        if (await dbContext.Shifts.AnyAsync(shift => shift.Code == code, cancellationToken))
        {
            throw new DomainRuleException("SHIFT_CODE_IN_USE", $"A shift with code '{code}' already exists.");
        }

        var shift = WorkShift.Create(code, request.Name, request.StartTime, request.EndTime, request.BreakMinutes, request.GraceMinutes, request.HalfDayMinutes, request.FullDayMinutes, request.WeeklyOff);
        bool first = !await dbContext.Shifts.AnyAsync(cancellationToken);
        if (first)
        {
            // The first shift a tenant defines becomes everyone's until they say otherwise.
            shift.MakeDefault();
        }

        dbContext.Shifts.Add(shift);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(shift);
    }

    public async Task<ShiftItem> UpdateAsync(Guid id, ShiftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkShift shift = await FindAsync(id, cancellationToken);
        shift.Update(request.Name, request.StartTime, request.EndTime, request.BreakMinutes, request.GraceMinutes, request.HalfDayMinutes, request.FullDayMinutes, request.WeeklyOff);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(shift);
    }

    public async Task<ShiftItem> MakeDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkShift shift = await FindAsync(id, cancellationToken);
        if (!shift.IsActive)
        {
            throw new DomainRuleException("SHIFT_INACTIVE", "Activate the shift before making it the default.");
        }

        // Two saves: clearing the old default first keeps the one-default index satisfied throughout.
        foreach (WorkShift current in await dbContext.Shifts.Where(candidate => candidate.IsDefault && candidate.Id != id).ToListAsync(cancellationToken))
        {
            current.ClearDefault();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        shift.MakeDefault();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(shift);
    }

    public async Task<ShiftItem> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        WorkShift shift = await FindAsync(id, cancellationToken);
        if (active)
        {
            shift.Activate();
        }
        else
        {
            shift.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(shift);
    }

    // ----- Assignments -------------------------------------------------------------------------

    public async Task<IReadOnlyList<AssignmentItem>> AssignmentsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        List<ShiftAssignment> rows = await dbContext.ShiftAssignments.AsNoTracking()
            .Where(assignment => assignment.EmployeeId == employeeId)
            .OrderByDescending(assignment => assignment.EffectiveFrom)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> names = await dbContext.Shifts.AsNoTracking().ToDictionaryAsync(shift => shift.Id, shift => shift.Name, cancellationToken);
        string? employeeName = (await employees.GetAsync(employeeId, cancellationToken))?.DisplayName;

        return [.. rows.Select(row => new AssignmentItem(row.Id, row.EmployeeId, employeeName, row.ShiftId, names.GetValueOrDefault(row.ShiftId) ?? string.Empty, row.EffectiveFrom, row.EffectiveTo))];
    }

    /// <summary>Puts people on a shift for a stretch, trimming or splitting what each had there before.</summary>
    public async Task<int> AssignAsync(AssignShiftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkShift shift = await FindAsync(request.ShiftId, cancellationToken);
        if (!shift.IsActive)
        {
            throw new DomainRuleException("SHIFT_INACTIVE", "Choose an active shift.");
        }

        if (request.EmployeeIds is null || request.EmployeeIds.Count is 0 or > 500)
        {
            throw new DomainRuleException("SHIFT_ASSIGN_PEOPLE", "Assign between one and five hundred people at a time.");
        }

        DateOnly from = request.EffectiveFrom;
        DateOnly? to = request.EffectiveTo;
        if (to is { } last && last < from)
        {
            throw new DomainRuleException("SHIFT_ASSIGNMENT_DATES", "An assignment cannot end before it starts.");
        }

        foreach (Guid employeeId in request.EmployeeIds.Distinct())
        {
            if (!await employees.IsActiveAsync(employeeId, cancellationToken))
            {
                throw new DomainRuleException("SHIFT_ASSIGN_INACTIVE", "Only current employees can be given a shift.");
            }

            List<ShiftAssignment> existing = await dbContext.ShiftAssignments
                .Where(assignment => assignment.EmployeeId == employeeId)
                .ToListAsync(cancellationToken);

            foreach (ShiftAssignment overlapping in existing.Where(assignment => assignment.Overlaps(from, to)))
            {
                DateOnly? originalEnd = overlapping.EffectiveTo;
                bool startsBefore = overlapping.EffectiveFrom < from;
                bool endsAfter = to is { } end && (originalEnd is null || originalEnd > end);

                if (startsBefore && endsAfter)
                {
                    overlapping.EndOn(from.AddDays(-1));
                    dbContext.ShiftAssignments.Add(ShiftAssignment.Create(employeeId, overlapping.ShiftId, to!.Value.AddDays(1), originalEnd));
                }
                else if (startsBefore)
                {
                    overlapping.EndOn(from.AddDays(-1));
                }
                else if (endsAfter)
                {
                    overlapping.StartOn(to!.Value.AddDays(1));
                }
                else
                {
                    dbContext.ShiftAssignments.Remove(overlapping);
                }
            }

            dbContext.ShiftAssignments.Add(ShiftAssignment.Create(employeeId, shift.Id, from, to));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return request.EmployeeIds.Distinct().Count();
    }

    // ----- Overrides ---------------------------------------------------------------------------

    public async Task SetOverrideAsync(OverrideRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShiftId is { } shiftId && !(await FindAsync(shiftId, cancellationToken)).IsActive)
        {
            throw new DomainRuleException("SHIFT_INACTIVE", "Choose an active shift.");
        }

        if (!await employees.IsActiveAsync(request.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("SHIFT_ASSIGN_INACTIVE", "Only current employees have a roster.");
        }

        await UpsertOverrideAsync(request.EmployeeId, request.Date, request.ShiftId, request.Reason, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearOverrideAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        RosterOverride? entry = await dbContext.RosterOverrides.FirstOrDefaultAsync(row => row.EmployeeId == employeeId && row.Date == date, cancellationToken);
        if (entry is not null)
        {
            dbContext.RosterOverrides.Remove(entry);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    // ----- Swaps -------------------------------------------------------------------------------

    public async Task<SwapItem> RequestSwapAsync(SwapRequestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("SHIFT_NO_EMPLOYEE", "Your account is not linked to an employee record.");

        if (body.Date < await clock.TodayAsync(cancellationToken))
        {
            throw new DomainRuleException("SHIFT_SWAP_PAST", "Only today or a future day can be swapped.");
        }

        if (!await employees.IsActiveAsync(body.CounterpartEmployeeId, cancellationToken))
        {
            throw new DomainRuleException("SHIFT_SWAP_COUNTERPART", "Swap with a current colleague.");
        }

        if (await dbContext.ShiftSwapRequests.AnyAsync(swap => swap.RequesterEmployeeId == me && swap.Date == body.Date && swap.Status == SwapStatus.Pending, cancellationToken))
        {
            throw new DomainRuleException("SHIFT_SWAP_PENDING", "You already have a swap waiting for that day.");
        }

        IReadOnlyList<ScheduledDay> days = await GetAsync([me, body.CounterpartEmployeeId], body.Date, body.Date, cancellationToken);
        ScheduledDay mine = days.Single(day => day.EmployeeId == me);
        ScheduledDay theirs = days.Single(day => day.EmployeeId == body.CounterpartEmployeeId);
        if (mine.ShiftId == theirs.ShiftId && mine.IsWeeklyOff == theirs.IsWeeklyOff)
        {
            throw new DomainRuleException("SHIFT_SWAP_SAME", "You are both on the same schedule that day; there is nothing to swap.");
        }

        var swap = ShiftSwapRequest.Create(me, body.CounterpartEmployeeId, body.Date, body.Reason);
        dbContext.ShiftSwapRequests.Add(swap);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SwapSubjectType,
                SubjectId = swap.Id,
                Title = $"Shift swap on {body.Date:d MMM yyyy}",
                RequesterEmployeeId = me,
                Link = "/me/attendance",
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            swap.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            await ApplySwapAsync(swap, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSwapItemsAsync([swap], cancellationToken))[0];
    }

    public async Task CancelSwapAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid me = currentUser.EmployeeId ?? Guid.Empty;
        ShiftSwapRequest swap = await dbContext.ShiftSwapRequests.FirstOrDefaultAsync(row => row.Id == id && row.RequesterEmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(ShiftSwapRequest), id);

        swap.Cancel();
        await workflow.WithdrawAsync(SwapSubjectType, swap.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SwapItem>> SwapsAsync(bool everyone, CancellationToken cancellationToken)
    {
        IQueryable<ShiftSwapRequest> swaps = dbContext.ShiftSwapRequests.AsNoTracking();
        if (!everyone)
        {
            Guid me = currentUser.EmployeeId ?? Guid.Empty;
            swaps = swaps.Where(swap => swap.RequesterEmployeeId == me || swap.CounterpartEmployeeId == me);
        }

        return await ToSwapItemsAsync(await swaps.OrderByDescending(swap => swap.Date).Take(300).ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Approval came through (or none was needed): each takes the other's schedule for the day.</summary>
    public async Task CompleteSwapAsync(Guid swapId, bool approved, CancellationToken cancellationToken)
    {
        ShiftSwapRequest? swap = await dbContext.ShiftSwapRequests.FirstOrDefaultAsync(row => row.Id == swapId, cancellationToken);
        if (swap is null || swap.Status != SwapStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            await ApplySwapAsync(swap, cancellationToken);
        }
        else
        {
            swap.Reject();
        }
    }

    private async Task ApplySwapAsync(ShiftSwapRequest swap, CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduledDay> days = await GetAsync([swap.RequesterEmployeeId, swap.CounterpartEmployeeId], swap.Date, swap.Date, cancellationToken);
        ScheduledDay requester = days.Single(day => day.EmployeeId == swap.RequesterEmployeeId);
        ScheduledDay counterpart = days.Single(day => day.EmployeeId == swap.CounterpartEmployeeId);

        await UpsertOverrideAsync(swap.RequesterEmployeeId, swap.Date, counterpart.IsWeeklyOff ? null : counterpart.ShiftId, "Shift swap", cancellationToken);
        await UpsertOverrideAsync(swap.CounterpartEmployeeId, swap.Date, requester.IsWeeklyOff ? null : requester.ShiftId, "Shift swap", cancellationToken);
        swap.Approve();
    }

    private async Task UpsertOverrideAsync(Guid employeeId, DateOnly date, Guid? shiftId, string? reason, CancellationToken cancellationToken)
    {
        RosterOverride? entry = await dbContext.RosterOverrides.FirstOrDefaultAsync(row => row.EmployeeId == employeeId && row.Date == date, cancellationToken);
        if (entry is null)
        {
            dbContext.RosterOverrides.Add(RosterOverride.Create(employeeId, date, shiftId, reason));
        }
        else
        {
            entry.Change(shiftId, reason);
        }
    }

    private async Task<List<SwapItem>> ToSwapItemsAsync(List<ShiftSwapRequest> swaps, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> names = (await employees.GetManyAsync(
                swaps.SelectMany(swap => new[] { swap.RequesterEmployeeId, swap.CounterpartEmployeeId }),
                cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. swaps.Select(swap => new SwapItem(
            swap.Id,
            swap.RequesterEmployeeId,
            names.GetValueOrDefault(swap.RequesterEmployeeId),
            swap.CounterpartEmployeeId,
            names.GetValueOrDefault(swap.CounterpartEmployeeId),
            swap.Date,
            swap.Reason,
            swap.Status,
            swap.WorkflowInstanceId,
            swap.CreatedDate))];
    }

    private async Task<WorkShift> FindAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Shifts.FirstOrDefaultAsync(shift => shift.Id == id, cancellationToken)
            ?? throw new NotFoundException("Shift", id);

    private static ScheduledDay Scheduled(Guid employeeId, DateOnly day, WorkShift shift, bool weeklyOff)
        => new(
            employeeId,
            day,
            shift == Standard ? null : shift.Id,
            shift.Name,
            shift.StartTime,
            shift.EndTime,
            shift.BreakMinutes,
            shift.GraceMinutes,
            shift.HalfDayMinutes,
            shift.FullDayMinutes,
            weeklyOff);

    private static ScheduledDay DayOff(Guid employeeId, DateOnly day, WorkShift basis)
        => Scheduled(employeeId, day, basis, weeklyOff: true) with { ShiftId = null, ShiftName = "Day off" };

    private static ShiftItem ToItem(WorkShift shift)
        => new(
            shift.Id, shift.Code, shift.Name, shift.StartTime, shift.EndTime, shift.BreakMinutes, shift.GraceMinutes,
            shift.HalfDayMinutes, shift.FullDayMinutes, shift.WeeklyOffDays, shift.IsDefault, shift.IsActive);
}

internal sealed class ApplySwapOnApproval(ShiftService shifts) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ShiftService.SwapSubjectType)
        {
            await shifts.CompleteSwapAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectSwapOnRejection(ShiftService shifts) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ShiftService.SwapSubjectType)
        {
            await shifts.CompleteSwapAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
