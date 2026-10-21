using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

public sealed record LeaveApplication(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StartsSecondHalf,
    bool EndsFirstHalf,
    string? Reason,
    Guid? EmployeeId = null);

public sealed record LeavePreview(decimal Days, decimal Available, bool WithinBalance, IReadOnlyList<string> Problems);

public sealed record LeaveRequestItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    string Color,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StartsSecondHalf,
    bool EndsFirstHalf,
    decimal Days,
    string? Reason,
    LeaveRequestStatus Status,
    Guid? WorkflowInstanceId,
    DateTime AppliedDate,
    DateTime? DecidedDate,
    string? CancellationReason);

public sealed record LeaveBalance(
    Guid LeaveTypeId,
    string Code,
    string Name,
    string Color,
    bool IsPaid,
    decimal Balance,
    decimal Pending,
    decimal Available,
    decimal AnnualEntitlement,
    decimal TakenThisYear);

public sealed record LedgerItem(Guid Id, DateOnly EntryDate, LedgerKind Kind, decimal Days, string? Note, Guid? RequestId);

public sealed record AdjustmentRequest(Guid EmployeeId, Guid LeaveTypeId, decimal Days, string Note);

public sealed record LeaveRequestQuery
{
    public Guid? EmployeeId { get; init; }

    public LeaveRequestStatus? Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

/// <summary>
/// Leave requests and balances (spec §20). Every rule a policy sets is checked when applying: the
/// probation and notice rules, the longest stretch, overlaps, and the balance after other pending
/// requests. Approval goes through Workflow; the days leave the balance when it is approved.
/// </summary>
internal sealed class LeaveService(
    AppDbContext dbContext,
    LeaveCalendarService calendar,
    IEmployeeDirectory employees,
    IWorkflowStarter workflow,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider,
    IEnumerable<ILeaveApplicationRule> applicationRules)
{
    public const string SubjectType = "leave.request";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Leave request",
        "leave",
        [
            new WorkflowSubjectField("days", "Days", WorkflowFieldType.Number),
            new WorkflowSubjectField("leaveType", "Leave type code", WorkflowFieldType.Text),
            new WorkflowSubjectField("isPaid", "Paid (true/false)", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Applying ----------------------------------------------------------------------------

    public async Task<LeavePreview> PreviewAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        Guid employeeId = await ResolveEmployeeAsync(application, onBehalf, cancellationToken);
        (decimal days, decimal available, List<string> problems) = await EvaluateAsync(employeeId, application, onBehalf, excludeRequestId: null, cancellationToken);
        return new LeavePreview(days, available, days <= available, problems);
    }

    public async Task<LeaveRequestItem> ApplyAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        Guid employeeId = await ResolveEmployeeAsync(application, onBehalf, cancellationToken);
        (decimal days, _, List<string> problems) = await EvaluateAsync(employeeId, application, onBehalf, excludeRequestId: null, cancellationToken);

        if (problems.Count > 0)
        {
            throw new DomainRuleException("LEAVE_NOT_ALLOWED", problems[0]) { Context = new Dictionary<string, object?> { ["problems"] = problems } };
        }

        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstAsync(row => row.Id == application.LeaveTypeId, cancellationToken);
        var request = LeaveRequest.Apply(
            employeeId, type.Id, application.StartDate, application.EndDate, application.StartsSecondHalf, application.EndsFirstHalf,
            days, application.Reason, onBehalf ? currentUser.UserId : null);
        dbContext.LeaveRequests.Add(request);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = request.Id,
                Title = $"{type.Name}: {days.ToString("0.#", CultureInfo.InvariantCulture)} day(s) from {application.StartDate:d MMM}",
                RequesterEmployeeId = employeeId,
                Link = "/team/leave",
                Fields = new Dictionary<string, string>
                {
                    ["days"] = days.ToString(CultureInfo.InvariantCulture),
                    ["leaveType"] = type.Code,
                    ["isPaid"] = type.IsPaid ? "true" : "false",
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            request.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            Approve(request, type);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([request], cancellationToken))[0];
    }

    /// <summary>Approval came through: the request is approved and its days leave the balance.</summary>
    public async Task CompleteAsync(Guid requestId, bool approved, CancellationToken cancellationToken)
    {
        LeaveRequest? request = await dbContext.LeaveRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        if (request is null || request.Status != LeaveRequestStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstAsync(row => row.Id == request.LeaveTypeId, cancellationToken);
            Approve(request, type);
        }
        else
        {
            request.Reject(Now);
        }
    }

    /// <summary>The employee (or, past the start, someone with leave.request.cancel) takes a request back.</summary>
    public async Task CancelAsync(Guid requestId, string? reason, CancellationToken cancellationToken)
    {
        LeaveRequest request = await dbContext.LeaveRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), requestId);

        bool own = currentUser.EmployeeId == request.EmployeeId;
        bool administrator = await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.RequestCancel, cancellationToken);
        if (!own && !administrator)
        {
            throw new NotFoundException(nameof(LeaveRequest), requestId);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (request.Status == LeaveRequestStatus.Approved && request.StartDate <= today && !administrator)
        {
            throw new DomainRuleException("LEAVE_ALREADY_STARTED", "Leave that has started can only be cancelled by HR.");
        }

        bool wasApproved = request.Status == LeaveRequestStatus.Approved;
        request.Cancel(Now, reason);

        if (wasApproved)
        {
            dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, today, LedgerKind.Restored, request.Days, request.Id, note: "Leave cancelled"));
            publisher.Publish(new LeaveCancelledIntegrationEvent(request.Id, request.EmployeeId, request.StartDate, request.EndDate));
        }
        else
        {
            await workflow.WithdrawAsync(SubjectType, request.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Reading -----------------------------------------------------------------------------

    public async Task<PagedResult<LeaveRequestItem>> ListAsync(LeaveRequestQuery query, bool mine, CancellationToken cancellationToken)
    {
        IQueryable<LeaveRequest> requests = dbContext.LeaveRequests.AsNoTracking();

        if (mine)
        {
            Guid me = currentUser.EmployeeId ?? Guid.Empty;
            requests = requests.Where(request => request.EmployeeId == me);
        }
        else
        {
            requests = await ScopeAsync(requests, query.EmployeeId, Permissions.Leave.RequestView, cancellationToken);
        }

        if (query.Status is { } status)
        {
            requests = requests.Where(request => request.Status == status);
        }

        if (query.From is { } from)
        {
            requests = requests.Where(request => request.EndDate >= from);
        }

        if (query.To is { } to)
        {
            requests = requests.Where(request => request.StartDate <= to);
        }

        PageRequest page = query.ToPageRequest();
        long total = await requests.LongCountAsync(cancellationToken);
        List<LeaveRequest> rows = await requests.OrderByDescending(request => request.StartDate).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return PagedResult.From(await ToItemsAsync(rows, cancellationToken), page, total);
    }

    /// <summary>Approved and pending leave in a window, for the team calendar.</summary>
    public async Task<IReadOnlyList<LeaveRequestItem>> CalendarAsync(DateOnly from, DateOnly to, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 92)
        {
            throw new DomainRuleException("LEAVE_CALENDAR_RANGE", "The calendar shows at most three months.");
        }

        IQueryable<LeaveRequest> requests = await ScopeAsync(dbContext.LeaveRequests.AsNoTracking(), null, Permissions.Leave.RequestView, cancellationToken);
        if (departmentId is { } department)
        {
            List<Guid> inDepartment = [.. await employees.ListActiveIdsAsync([department], null, cancellationToken)];
            requests = requests.Where(request => inDepartment.Contains(request.EmployeeId));
        }

        List<LeaveRequest> rows = await requests
            .Where(request => (request.Status == LeaveRequestStatus.Approved || request.Status == LeaveRequestStatus.Pending)
                && request.StartDate <= to && request.EndDate >= from)
            .OrderBy(request => request.StartDate)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return await ToItemsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<LeaveBalance>> BalancesAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var yearStart = new DateOnly(today.Year, 1, 1);

        List<LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => type.IsActive).OrderBy(type => type.Name).ToListAsync(cancellationToken);
        List<LeavePolicy> policies = await dbContext.LeavePolicies.AsNoTracking().Where(policy => policy.IsActive).ToListAsync(cancellationToken);

        var ledger = await dbContext.LeaveLedger.AsNoTracking()
            .Where(entry => entry.EmployeeId == employeeId)
            .GroupBy(entry => entry.LeaveTypeId)
            .Select(group => new
            {
                group.Key,
                Balance = group.Sum(entry => entry.Days),
                Taken = group.Where(entry => entry.Kind == LedgerKind.Taken && entry.EntryDate >= yearStart).Sum(entry => -entry.Days)
                    - group.Where(entry => entry.Kind == LedgerKind.Restored && entry.EntryDate >= yearStart).Sum(entry => entry.Days),
            })
            .ToDictionaryAsync(row => row.Key, cancellationToken);

        Dictionary<Guid, decimal> pending = await dbContext.LeaveRequests.AsNoTracking()
            .Where(request => request.EmployeeId == employeeId && request.Status == LeaveRequestStatus.Pending)
            .GroupBy(request => request.LeaveTypeId)
            .Select(group => new { group.Key, Days = group.Sum(request => request.Days) })
            .ToDictionaryAsync(row => row.Key, row => row.Days, cancellationToken);

        var balances = new List<LeaveBalance>();
        foreach (LeaveType type in types)
        {
            LeavePolicy? policy = PolicyFor(policies, type.Id, person.EmploymentTypeCode);
            if (policy is null && !ledger.ContainsKey(type.Id))
            {
                continue;
            }

            decimal balance = ledger.TryGetValue(type.Id, out var row) ? row.Balance : 0;
            decimal held = pending.GetValueOrDefault(type.Id);
            balances.Add(new LeaveBalance(
                type.Id, type.Code, type.Name, type.Color, type.IsPaid, balance, held, balance - held,
                policy?.AnnualEntitlement ?? 0, row?.Taken ?? 0));
        }

        return balances;
    }

    public async Task<IReadOnlyList<LedgerItem>> LedgerAsync(Guid employeeId, Guid? leaveTypeId, CancellationToken cancellationToken)
    {
        IQueryable<LeaveLedgerEntry> entries = dbContext.LeaveLedger.AsNoTracking().Where(entry => entry.EmployeeId == employeeId);
        if (leaveTypeId is { } typeId)
        {
            entries = entries.Where(entry => entry.LeaveTypeId == typeId);
        }

        return await entries
            .OrderByDescending(entry => entry.EntryDate).ThenByDescending(entry => entry.CreatedDate)
            .Take(500)
            .Select(entry => new LedgerItem(entry.Id, entry.EntryDate, entry.Kind, entry.Days, entry.Note, entry.RequestId))
            .ToListAsync(cancellationToken);
    }

    public async Task AdjustAsync(AdjustmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Days == 0 || Math.Abs(request.Days) > 366 || decimal.Round(request.Days * 2) != request.Days * 2)
        {
            throw new DomainRuleException("LEAVE_ADJUSTMENT_DAYS", "Adjust by whole or half days, up to a year either way.");
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            throw new DomainRuleException("LEAVE_ADJUSTMENT_NOTE", "Say why the balance is being adjusted.");
        }

        if (!await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.BalanceAdjust, cancellationToken))
        {
            throw new NotFoundException("Employee", request.EmployeeId);
        }

        if (!await dbContext.LeaveTypes.AnyAsync(type => type.Id == request.LeaveTypeId, cancellationToken))
        {
            throw new NotFoundException(nameof(LeaveType), request.LeaveTypeId);
        }

        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(
            request.EmployeeId, request.LeaveTypeId, await clock.TodayAsync(cancellationToken),
            request.Days > 0 ? LedgerKind.Grant : LedgerKind.Adjustment, request.Days, note: request.Note));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Internals ---------------------------------------------------------------------------

    /// <summary>
    /// Days the application takes, the balance available for it, and every rule it breaks. HR
    /// applying on someone's behalf is not held to the notice period.
    /// </summary>
    private async Task<(decimal Days, decimal Available, List<string> Problems)> EvaluateAsync(
        Guid employeeId,
        LeaveApplication application,
        bool onBehalf,
        Guid? excludeRequestId,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (application.EndDate < application.StartDate)
        {
            throw new DomainRuleException("LEAVE_DATES", "Leave cannot end before it starts.");
        }

        if (application.EndDate.DayNumber - application.StartDate.DayNumber > 365)
        {
            throw new DomainRuleException("LEAVE_TOO_LONG", "Apply for at most a year at a time.");
        }

        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == application.LeaveTypeId && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("LEAVE_TYPE_UNKNOWN", "Choose an active leave type.");

        if (!type.AllowHalfDay && (application.StartsSecondHalf || application.EndsFirstHalf))
        {
            throw new DomainRuleException("LEAVE_NO_HALF_DAYS", $"{type.Name} is taken in whole days.");
        }

        // Rules other modules add (notice period restrictions and the like).
        foreach (ILeaveApplicationRule rule in applicationRules)
        {
            if (await rule.CheckAsync(employeeId, type.Code, application.StartDate, application.EndDate, cancellationToken) is { } problem)
            {
                problems.Add(problem);
            }
        }

        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        List<LeavePolicy> policies = await dbContext.LeavePolicies.AsNoTracking().Where(policy => policy.IsActive && policy.LeaveTypeId == type.Id).ToListAsync(cancellationToken);
        LeavePolicy policy = PolicyFor(policies, type.Id, person.EmploymentTypeCode)
            ?? throw new DomainRuleException("LEAVE_NO_POLICY", $"No {type.Name} policy covers this employee.");

        Dictionary<(Guid EmployeeId, DateOnly Day), bool> working = await calendar.WorkingDaysAsync([employeeId], application.StartDate, application.EndDate, cancellationToken);
        List<LeaveCalendarDay> days = [];
        for (DateOnly day = application.StartDate; day <= application.EndDate; day = day.AddDays(1))
        {
            days.Add(new LeaveCalendarDay(day, working.GetValueOrDefault((employeeId, day), true)));
        }

        decimal count = LeaveCalculator.CountDays(days, application.StartsSecondHalf, application.EndsFirstHalf, policy.CountSandwichedDays);
        if (count <= 0)
        {
            problems.Add("Those dates fall on weekly offs or holidays; there is nothing to take.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        bool onProbation = person.ConfirmationDate is null && person.ProbationEndDate is { } probationEnd && probationEnd >= application.StartDate;
        if (onProbation && !policy.AllowDuringProbation)
        {
            problems.Add($"{type.Name} cannot be taken during probation.");
        }

        if (!onBehalf && policy.MinNoticeDays > 0 && application.StartDate < today.AddDays(policy.MinNoticeDays))
        {
            problems.Add($"{type.Name} needs {policy.MinNoticeDays} day(s) of notice.");
        }

        if (policy.MaxConsecutiveDays is { } longest && count > longest)
        {
            problems.Add($"{type.Name} can be taken for at most {longest} day(s) at a time.");
        }

        bool overlaps = await dbContext.LeaveRequests.AnyAsync(
            request => request.EmployeeId == employeeId
                && request.Id != excludeRequestId
                && (request.Status == LeaveRequestStatus.Pending || request.Status == LeaveRequestStatus.Approved)
                && request.StartDate <= application.EndDate
                && request.EndDate >= application.StartDate,
            cancellationToken);
        if (overlaps)
        {
            problems.Add("These dates overlap leave already requested.");
        }

        decimal balance = await dbContext.LeaveLedger.Where(entry => entry.EmployeeId == employeeId && entry.LeaveTypeId == type.Id).SumAsync(entry => entry.Days, cancellationToken);
        decimal pending = await dbContext.LeaveRequests
            .Where(request => request.EmployeeId == employeeId && request.LeaveTypeId == type.Id && request.Status == LeaveRequestStatus.Pending && request.Id != excludeRequestId)
            .SumAsync(request => request.Days, cancellationToken);
        decimal available = balance - pending;

        if (count > available + policy.MaxNegativeBalance)
        {
            problems.Add($"Not enough {type.Name}: {available.ToString("0.#", CultureInfo.InvariantCulture)} day(s) available.");
        }

        return (count, available, problems);
    }

    private async Task<Guid> ResolveEmployeeAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!onBehalf)
        {
            return currentUser.EmployeeId ?? throw new DomainRuleException("LEAVE_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        }

        Guid employeeId = application.EmployeeId ?? throw new DomainRuleException("LEAVE_EMPLOYEE_REQUIRED", "Choose whose leave this is.");
        return await employees.IsInScopeAsync(employeeId, Permissions.Leave.RequestCreateOnBehalf, cancellationToken)
            ? employeeId
            : throw new NotFoundException("Employee", employeeId);
    }

    private void Approve(LeaveRequest request, LeaveType type)
    {
        request.Approve(Now);
        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, request.StartDate, LedgerKind.Taken, -request.Days, request.Id, note: type.Name));
        publisher.Publish(new LeaveApprovedIntegrationEvent(request.Id, request.EmployeeId, request.StartDate, request.EndDate, request.Days, type.Code));
    }

    private async Task<IQueryable<LeaveRequest>> ScopeAsync(IQueryable<LeaveRequest> requests, Guid? employeeId, string permission, CancellationToken cancellationToken)
    {
        if (employeeId is { } single)
        {
            if (!await employees.IsInScopeAsync(single, permission, cancellationToken))
            {
                throw new NotFoundException("Employee", single);
            }

            return requests.Where(request => request.EmployeeId == single);
        }

        IReadOnlyCollection<Guid>? visible = await employees.InScopeIdsAsync(permission, cancellationToken);
        if (visible is null)
        {
            return requests;
        }

        List<Guid> ids = [.. visible];
        return requests.Where(request => ids.Contains(request.EmployeeId));
    }

    private async Task<List<LeaveRequestItem>> ToItemsAsync(List<LeaveRequest> requests, CancellationToken cancellationToken)
    {
        List<Guid> typeIds = [.. requests.Select(request => request.LeaveTypeId).Distinct()];
        Dictionary<Guid, LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => typeIds.Contains(type.Id)).ToDictionaryAsync(type => type.Id, cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(requests.Select(request => request.EmployeeId), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. requests.Select(request =>
        {
            LeaveType? type = types.GetValueOrDefault(request.LeaveTypeId);
            return new LeaveRequestItem(
                request.Id,
                request.EmployeeId,
                names.GetValueOrDefault(request.EmployeeId),
                request.LeaveTypeId,
                type?.Code ?? string.Empty,
                type?.Name ?? string.Empty,
                type?.Color ?? "#546E7A",
                request.StartDate,
                request.EndDate,
                request.StartsSecondHalf,
                request.EndsFirstHalf,
                request.Days,
                request.Reason,
                request.Status,
                request.WorkflowInstanceId,
                request.CreatedDate == default ? Now : request.CreatedDate,
                request.DecidedDate,
                request.CancellationReason);
        })];
    }

    /// <summary>The employment type's own policy if it has one, else the general one.</summary>
    internal static LeavePolicy? PolicyFor(IEnumerable<LeavePolicy> policies, Guid leaveTypeId, string? employmentTypeCode)
    {
        List<LeavePolicy> forType = [.. policies.Where(policy => policy.IsActive && policy.LeaveTypeId == leaveTypeId)];
        return forType.FirstOrDefault(policy => policy.EmploymentTypeCode is not null
                && string.Equals(policy.EmploymentTypeCode, employmentTypeCode, StringComparison.OrdinalIgnoreCase))
            ?? forType.FirstOrDefault(policy => policy.EmploymentTypeCode is null);
    }
}

internal sealed class ApproveLeaveOnApproval(LeaveService leave) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == LeaveService.SubjectType)
        {
            await leave.CompleteAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectLeaveOnRejection(LeaveService leave) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == LeaveService.SubjectType)
        {
            await leave.CompleteAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
