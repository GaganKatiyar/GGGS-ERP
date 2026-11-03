using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Timesheets.Domain;
using Gggs.Modules.Timesheets.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Timesheets.Application;

public sealed record EntryItem(Guid Id, DateOnly Date, Guid? ProjectId, string? ProjectCode, Guid? TaskId, string? TaskCode, HourCategory Category, decimal Hours, string? Activity, string? Description);

public sealed record DayItem(
    DateOnly Date,
    bool IsWorkingDay,
    string? Holiday,
    string? Leave,
    decimal Hours,
    decimal LeaveHours,
    decimal HolidayHours,
    string? AttendanceStatus,
    int? WorkedMinutes);

public sealed record TimesheetHeader(
    Guid? Id,
    Guid EmployeeId,
    string? EmployeeName,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    TimesheetStatus Status,
    decimal TotalHours,
    decimal BillableHours,
    DateTime? SubmittedDate,
    DateTime? DecidedDate,
    bool IsLate,
    Guid? WorkflowInstanceId,
    string? Comment,
    int CorrectionCount,
    string? LastCorrectionReason,
    bool IsLocked);

/// <summary>One approver's share of a week. No project means default-project and other non-project hours.</summary>
public sealed record ApprovalItem(
    Guid Id,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    ApprovalStatus Status,
    decimal Hours,
    decimal BillableHours,
    Guid? WorkflowInstanceId,
    DateTime? DecidedDate);

public sealed record TimesheetView(
    TimesheetHeader Timesheet,
    IReadOnlyList<EntryItem> Entries,
    IReadOnlyList<DayItem> Days,
    IReadOnlyList<TimesheetTarget> Targets,
    IReadOnlyList<TimesheetIssue> Issues,
    IReadOnlyDictionary<string, decimal> ByCategory,
    decimal OvertimeHours,
    TimesheetRules Rules,
    IReadOnlyList<ApprovalItem> Approvals);

public sealed record EntryBody(DateOnly Date, Guid? ProjectId, Guid? TaskId, HourCategory Category, decimal Hours, string? Activity, string? Description);

public sealed record WeekBody(IReadOnlyList<EntryBody> Entries, string? Comment);

public sealed record SubmitOutcome(TimesheetStatus Status, Guid? WorkflowInstanceId, IReadOnlyList<TimesheetIssue> Issues);

/// <summary>Reopens one share of the week, or every approved share when none is named.</summary>
public sealed record ReopenBody(string Reason, Guid? ApprovalId = null);

public sealed record ApprovalQuery
{
    public Guid? ProjectId { get; init; }

    public ApprovalStatus? Status { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? 50 };
}

/// <summary>A share of someone's week, as its approver sees it.</summary>
public sealed record ApprovalRow(
    Guid Id,
    Guid TimesheetId,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    DateOnly WeekStart,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? OwnerName,
    ApprovalStatus Status,
    decimal Hours,
    decimal BillableHours,
    DateTime SubmittedDate,
    Guid? WorkflowInstanceId);

public sealed record LockBody(DateOnly Through);

public sealed record TimesheetListQuery
{
    public DateOnly? WeekStart { get; init; }

    public TimesheetStatus? Status { get; init; }

    public Guid? EmployeeId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? 50 };
}

public sealed record TimesheetRow(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    DateOnly WeekStart,
    TimesheetStatus Status,
    decimal TotalHours,
    decimal BillableHours,
    DateTime? SubmittedDate,
    bool IsLate,
    int CorrectionCount);

public sealed record MissingRow(Guid EmployeeId, string? EmployeeName, string? EmployeeCode, string? Designation, TimesheetStatus? Status);

public sealed record ProjectHoursRow(Guid EmployeeId, string? EmployeeName, Guid? TaskId, HourCategory Category, decimal Hours, decimal ApprovedHours);

public sealed record BankQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public TimesheetStatus? Status { get; init; }

    public Guid? ProjectId { get; init; }
}

/// <summary>One project's share of a week in the bank; no project means hours booked to none.</summary>
public sealed record BankProject(Guid? ProjectId, string? ProjectCode, string? ProjectName, decimal Hours, decimal BillableHours, string? ApproverName, ApprovalStatus? Status);

public sealed record BankWeek(
    Guid Id,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    TimesheetStatus Status,
    decimal WorkingHours,
    decimal LeaveHours,
    decimal HolidayHours,
    bool IsLate,
    IReadOnlyList<BankProject> Projects);

/// <summary>
/// Timesheets (spec §70-71): a person's week against the projects they are on, checked against the
/// tenant's rules, submitted through approval, reopened for corrections, and locked by period.
/// </summary>
internal sealed class TimesheetService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IProjectDirectory projects,
    IProjectPermissions projectPermissions,
    IOrgStructure org,
    IShiftSchedule shifts,
    ILeaveCalendar leave,
    IAttendanceLedger attendance,
    IWorkflowStarter workflow,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string SubjectType = "timesheet.submission";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a timesheet",
        "timesheets",
        [
            new WorkflowSubjectField("totalHours", "Total hours", WorkflowFieldType.Number),
            new WorkflowSubjectField("billableHours", "Billable hours", WorkflowFieldType.Number),
            new WorkflowSubjectField("overtimeHours", "Overtime hours", WorkflowFieldType.Number),
            new WorkflowSubjectField("late", "Submitted late (true/false)", WorkflowFieldType.Text),
        ])
    {
        OwnerApprovesByDefault = true,
    };

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- My week ------------------------------------------------------------------------------

    public async Task<TimesheetView> MyWeekAsync(DateOnly? week, CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly weekStart = Timesheet.WeekOf(week ?? await clock.TodayAsync(cancellationToken));
        return await ViewAsync(me, weekStart, withTargets: true, cancellationToken);
    }

    public async Task<TimesheetView> SaveMyWeekAsync(DateOnly week, WeekBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        DateOnly weekStart = Timesheet.WeekOf(week);
        TimesheetRules rules = await RulesAsync(cancellationToken);

        Timesheet timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.EmployeeId == me && row.WeekStart == weekStart, cancellationToken)
            ?? Track(Timesheet.Open(me, weekStart));
        timesheet.EnsureEditable(rules.LockedThrough);

        List<TimesheetEntry> existing = await dbContext.TimesheetEntries.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        HashSet<Guid?> locked = [.. (await dbContext.TimesheetApprovals.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken))
            .Where(row => row.IsLocked)
            .Select(row => row.ProjectId)];
        IReadOnlyDictionary<Guid, ProjectSummary> summaries = await projects.GetManyAsync(
            body.Entries.Select(entry => entry.ProjectId).Concat(existing.Select(row => row.ProjectId)).OfType<Guid>().Distinct(), cancellationToken);

        IReadOnlyDictionary<Guid, Guid> taskProjects = await projects.TaskProjectsAsync(body.Entries.Select(entry => entry.TaskId).OfType<Guid>(), cancellationToken);
        var onTeam = new Dictionary<(Guid, DateOnly), bool>();
        List<TimesheetEntry> entries = [];
        List<EntryBody> onLockedShares = [];
        foreach (EntryBody entry in body.Entries.Where(entry => entry.Hours > 0))
        {
            if (locked.Contains(ShareOf(entry.ProjectId, summaries)))
            {
                onLockedShares.Add(entry);
                continue;
            }

            if (entry.TaskId is { } taskId && (!taskProjects.TryGetValue(taskId, out Guid owner) || owner != entry.ProjectId))
            {
                throw new DomainRuleException("TIMESHEET_TASK_PROJECT", "A task is booked under the project it belongs to.");
            }

            if (entry.ProjectId is { } projectId)
            {
                if (!onTeam.TryGetValue((projectId, entry.Date), out bool allowed))
                {
                    allowed = await projects.IsOnTeamAsync(projectId, me, entry.Date, cancellationToken);
                    onTeam[(projectId, entry.Date)] = allowed;
                }

                if (!allowed)
                {
                    throw new DomainRuleException("TIMESHEET_NOT_ON_PROJECT", $"You were not on that project on {entry.Date:d MMM}.");
                }
            }

            entries.Add(TimesheetEntry.Book(timesheet, entry.Date, entry.ProjectId, entry.TaskId, entry.Category, entry.Hours, entry.Activity, entry.Description));
        }

        List<TimesheetEntry> kept = [.. existing.Where(row => locked.Contains(ShareOf(row.ProjectId, summaries)))];
        if (!SameHours(kept.Select(row => (row.Date, row.ProjectId, row.TaskId, row.Category, row.Hours)), onLockedShares.Select(row => (row.Date, row.ProjectId, row.TaskId, row.Category, row.Hours))))
        {
            throw new DomainRuleException("TIMESHEET_SHARE_LOCKED", "Hours that are approved or with their approver cannot change. Ask for them to be reopened, or withdraw the week.");
        }

        dbContext.TimesheetEntries.RemoveRange(existing.Except(kept));
        dbContext.TimesheetEntries.AddRange(entries);
        timesheet.Recalculate(kept.Concat(entries), body.Comment);

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(me, weekStart, withTargets: true, cancellationToken);
    }

    /// <summary>Starts this week from last week's rows, when this week is still empty.</summary>
    public async Task<TimesheetView> CopyPreviousWeekAsync(DateOnly week, CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly weekStart = Timesheet.WeekOf(week);
        if (await dbContext.TimesheetEntries.AnyAsync(row => row.EmployeeId == me && row.Date >= weekStart && row.Date <= weekStart.AddDays(6), cancellationToken))
        {
            throw new DomainRuleException("TIMESHEET_NOT_EMPTY", "This week already has hours; copy only starts an empty week.");
        }

        List<TimesheetEntry> previous = await dbContext.TimesheetEntries.AsNoTracking()
            .Where(row => row.EmployeeId == me && row.Date >= weekStart.AddDays(-7) && row.Date < weekStart)
            .ToListAsync(cancellationToken);

        var body = new WeekBody([.. previous.Select(row => new EntryBody(row.Date.AddDays(7), row.ProjectId, row.TaskId, row.Category, row.Hours, row.Activity, row.Description))], null);
        return await SaveMyWeekAsync(weekStart, body, cancellationToken);
    }

    public async Task<SubmitOutcome> SubmitMyWeekAsync(DateOnly week, CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly weekStart = Timesheet.WeekOf(week);
        Timesheet timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.EmployeeId == me && row.WeekStart == weekStart, cancellationToken)
            ?? throw new DomainRuleException("TIMESHEET_EMPTY", "Book some hours before submitting the week.");

        TimesheetRules rules = await RulesAsync(cancellationToken);
        timesheet.EnsureEditable(rules.LockedThrough);
        List<TimesheetEntry> entries = await dbContext.TimesheetEntries.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        IReadOnlyList<DayFacts> days = await DaysAsync(me, weekStart, cancellationToken);
        IReadOnlyList<TimesheetIssue> issues = TimesheetChecks.Check(rules, days, entries.Select(entry => (entry.Date, entry.Hours)));

        TimesheetIssue? blocking = issues.FirstOrDefault(issue => issue.Level == IssueLevel.Error);
        if (blocking is not null)
        {
            throw new DomainRuleException(blocking.Code, blocking.Message);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        bool late = TimesheetChecks.IsLate(timesheet.WeekEnd, today, rules.SubmissionDeadlineDays);
        timesheet.Submit(Now, late);
        await workflow.WithdrawAsync(SubjectType, timesheet.Id, cancellationToken);

        List<TimesheetApproval> shares = await dbContext.TimesheetApprovals.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ProjectSummary> summaries = await projects.GetManyAsync(entries.Select(entry => entry.ProjectId).OfType<Guid>().Distinct(), cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(me, cancellationToken);
        string overtime = TimesheetChecks.Overtime(rules, days, entries.Select(entry => (entry.Date, entry.Hours))).ToString(CultureInfo.InvariantCulture);

        var booked = entries.GroupBy(entry => ShareOf(entry.ProjectId, summaries)).ToList();
        foreach (var group in booked)
        {
            TimesheetApproval? share = shares.FirstOrDefault(row => row.ProjectId == group.Key);
            if (share?.IsLocked == true)
            {
                continue;
            }

            if (share is null)
            {
                share = TimesheetApproval.Open(timesheet, group.Key);
                dbContext.TimesheetApprovals.Add(share);
                shares.Add(share);
            }

            decimal hours = group.Sum(entry => entry.Hours);
            decimal billable = group.Where(entry => entry.Category == HourCategory.Billable).Sum(entry => entry.Hours);
            Guid? owner = await OwnerAsync(group.Key, person, summaries, cancellationToken);
            share.Submit(hours, billable, owner, Now);
            foreach (TimesheetEntry entry in group)
            {
                entry.AssignTo(share.Id);
            }

            await workflow.WithdrawAsync(SubjectType, share.Id, cancellationToken);
            string label = group.Key is { } project && summaries.TryGetValue(project, out ProjectSummary? summary) ? summary.Code : "non-project time";
            WorkflowStartResult started = await workflow.StartAsync(
                new WorkflowStartRequest
                {
                    SubjectType = SubjectType,
                    SubjectId = share.Id,
                    Title = $"Timesheet, {label}, week of {weekStart:d MMM yyyy} ({hours:0.##} h)",
                    RequesterEmployeeId = me,
                    OwnerEmployeeId = owner,
                    Link = $"/timesheets/approvals?open={timesheet.Id}",
                    Fields = new Dictionary<string, string>
                    {
                        ["totalHours"] = hours.ToString(CultureInfo.InvariantCulture),
                        ["billableHours"] = billable.ToString(CultureInfo.InvariantCulture),
                        ["overtimeHours"] = overtime,
                        ["late"] = late ? "true" : "false",
                    },
                },
                cancellationToken);

            if (started.Outcome == WorkflowStartOutcome.Pending)
            {
                share.AwaitApproval(started.InstanceId!.Value);
            }
            else
            {
                share.Approve(Now);
                PublishHoursApproved(share, group);
            }
        }

        // A share whose hours were all removed before submitting again has nothing left to approve.
        foreach (TimesheetApproval empty in shares.Where(row => !row.IsLocked && booked.All(group => group.Key != row.ProjectId)).ToList())
        {
            await workflow.WithdrawAsync(SubjectType, empty.Id, cancellationToken);
            dbContext.TimesheetApprovals.Remove(empty);
            shares.Remove(empty);
        }

        Settle(timesheet, shares, entries);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SubmitOutcome(timesheet.Status, timesheet.WorkflowInstanceId, issues);
    }

    public async Task WithdrawMyWeekAsync(DateOnly week, CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly weekStart = Timesheet.WeekOf(week);
        Timesheet timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.EmployeeId == me && row.WeekStart == weekStart, cancellationToken)
            ?? throw new NotFoundException(nameof(Timesheet), weekStart);
        if (timesheet.Status != TimesheetStatus.Submitted)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only a submitted week can be withdrawn.");
        }

        await workflow.WithdrawAsync(SubjectType, timesheet.Id, cancellationToken);
        List<TimesheetApproval> shares = await dbContext.TimesheetApprovals.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        if (shares.Count == 0)
        {
            timesheet.ReturnToDraft();
        }
        else
        {
            // Hours already approved stay approved; only what is still with an approver comes back.
            foreach (TimesheetApproval share in shares.Where(row => row.Status == ApprovalStatus.Submitted))
            {
                await workflow.WithdrawAsync(SubjectType, share.Id, cancellationToken);
                share.Return();
            }

            Settle(timesheet, shares, await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>My past weeks, newest first, each split by project (the Timesheet bank). At most a year at a time.</summary>
    public async Task<IReadOnlyList<BankWeek>> MyBankAsync(BankQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid me = Me();
        DateOnly to = query.To ?? await clock.TodayAsync(cancellationToken);
        DateOnly from = query.From ?? to.AddMonths(-6);
        if (to < from || to.DayNumber - from.DayNumber > 370)
        {
            throw new DomainRuleException("TIMESHEET_BANK_RANGE", "Choose a period of up to a year, ending after it starts.");
        }

        DateOnly firstWeek = Timesheet.WeekOf(from);
        DateOnly lastWeek = Timesheet.WeekOf(to);
        IQueryable<Timesheet> rows = dbContext.Timesheets.AsNoTracking().Where(row => row.EmployeeId == me && row.WeekStart >= firstWeek && row.WeekStart <= lastWeek);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        List<Timesheet> sheets = await rows.OrderByDescending(row => row.WeekStart).ToListAsync(cancellationToken);
        List<Guid> sheetIds = [.. sheets.Select(row => row.Id)];
        ILookup<Guid, TimesheetEntry> entries = (await dbContext.TimesheetEntries.AsNoTracking().Where(row => sheetIds.Contains(row.TimesheetId)).ToListAsync(cancellationToken))
            .ToLookup(row => row.TimesheetId);
        if (query.ProjectId is { } projectId)
        {
            sheets = [.. sheets.Where(sheet => entries[sheet.Id].Any(entry => entry.ProjectId == projectId))];
        }

        IReadOnlyDictionary<Guid, ProjectSummary> projectsById = await projects.GetManyAsync(entries.SelectMany(group => group).Select(entry => entry.ProjectId).OfType<Guid>(), cancellationToken);
        Dictionary<Guid, string> managers = (await employees.GetManyAsync(projectsById.Values.Select(project => project.ProjectManagerEmployeeId).OfType<Guid>(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        TimesheetRules rules = await RulesAsync(cancellationToken);
        IReadOnlyList<DayFacts> facts = await FactsAsync(me, firstWeek, lastWeek.AddDays(6), cancellationToken);
        ILookup<Guid, TimesheetApproval> shares = (await dbContext.TimesheetApprovals.AsNoTracking().Where(row => sheetIds.Contains(row.TimesheetId)).ToListAsync(cancellationToken))
            .ToLookup(row => row.TimesheetId);
        Dictionary<Guid, string> owners = (await employees.GetManyAsync(shares.SelectMany(group => group).Select(row => row.OwnerEmployeeId).OfType<Guid>(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. sheets.Select(sheet =>
        {
            List<DayFacts> week = [.. facts.Where(day => day.Date >= sheet.WeekStart && day.Date <= sheet.WeekEnd)];
            return new BankWeek(
                sheet.Id, sheet.WeekStart, sheet.WeekEnd, sheet.Status, sheet.TotalHours, week.Sum(day => LeaveHours(day, rules)), week.Sum(day => HolidayHours(day, rules)), sheet.IsLate,
                [.. entries[sheet.Id].GroupBy(entry => entry.ProjectId).Select(group =>
                {
                    ProjectSummary? project = group.Key is { } id ? projectsById.GetValueOrDefault(id) : null;
                    TimesheetApproval? share = shares[sheet.Id].FirstOrDefault(row => row.ProjectId == ShareOf(group.Key, projectsById));
                    string? approver = share?.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner)
                        : project?.ProjectManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager) : null;
                    return new BankProject(
                        group.Key, project?.Code, project?.Name, group.Sum(entry => entry.Hours), group.Where(entry => entry.Category == HourCategory.Billable).Sum(entry => entry.Hours),
                        approver, share?.Status);
                }).OrderBy(row => row.ProjectCode, StringComparer.Ordinal)]);
        })];
    }

    // ----- Others' timesheets -------------------------------------------------------------------

    public async Task<PagedResult<TimesheetRow>> ListAsync(TimesheetListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Timesheet.View, cancellationToken);
        IQueryable<Timesheet> rows = dbContext.Timesheets.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (query.WeekStart is { } week)
        {
            DateOnly weekStart = Timesheet.WeekOf(week);
            rows = rows.Where(row => row.WeekStart == weekStart);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.EmployeeId is { } employeeId)
        {
            rows = rows.Where(row => row.EmployeeId == employeeId);
        }

        PagedResult<Timesheet> result = await rows.OrderByDescending(row => row.WeekStart).ThenBy(row => row.EmployeeId).ToPagedResultAsync(page, row => row, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(result.Items.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return PagedResult.From<TimesheetRow>(
            [.. result.Items.Select(row => new TimesheetRow(
                row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode,
                row.WeekStart, row.Status, row.TotalHours, row.BillableHours, row.SubmittedDate, row.IsLate, row.CorrectionCount))],
            page,
            result.TotalCount);
    }

    public async Task<TimesheetView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Timesheet timesheet = await dbContext.Timesheets.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Timesheet), id);
        if (timesheet.EmployeeId == currentUser.EmployeeId || await employees.IsInScopeAsync(timesheet.EmployeeId, Permissions.Timesheet.View, cancellationToken))
        {
            return await ViewAsync(timesheet.EmployeeId, timesheet.WeekStart, withTargets: false, cancellationToken);
        }

        // An approver sees the share that came to them, and none of the rest of the person's week.
        List<Guid> mine = await dbContext.TimesheetApprovals.AsNoTracking()
            .Where(row => row.TimesheetId == id && row.OwnerEmployeeId == currentUser.EmployeeId)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        return mine.Count > 0
            ? await ViewAsync(timesheet.EmployeeId, timesheet.WeekStart, withTargets: false, cancellationToken, onlyShares: mine)
            : throw new NotFoundException(nameof(Timesheet), id);
    }

    /// <summary>Shares of people's weeks: those that came to me and those I can see, or everything on a project I can see.</summary>
    public async Task<PagedResult<ApprovalRow>> ApprovalsAsync(ApprovalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IQueryable<TimesheetApproval> rows = dbContext.TimesheetApprovals.AsNoTracking();
        Guid? me = currentUser.EmployeeId;

        if (query.ProjectId is { } projectId && await projectPermissions.CanAsync(projectId, Permissions.Project.View, cancellationToken))
        {
            rows = rows.Where(row => row.ProjectId == projectId);
        }
        else
        {
            IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Timesheet.View, cancellationToken);
            if (scope is not null)
            {
                rows = rows.Where(row => row.OwnerEmployeeId == me || scope.Contains(row.EmployeeId));
            }

            if (query.ProjectId is { } other)
            {
                rows = rows.Where(row => row.ProjectId == other);
            }
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        PagedResult<TimesheetApproval> result = await rows.OrderByDescending(row => row.WeekStart).ThenBy(row => row.ProjectId).ThenBy(row => row.EmployeeId).ToPagedResultAsync(page, row => row, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(result.Items.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.OwnerEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        IReadOnlyDictionary<Guid, ProjectSummary> summaries = await projects.GetManyAsync(result.Items.Select(row => row.ProjectId).OfType<Guid>().Distinct(), cancellationToken);

        return PagedResult.From<ApprovalRow>(
            [.. result.Items.Select(row => new ApprovalRow(
                row.Id, row.TimesheetId, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode, row.WeekStart,
                row.ProjectId, row.ProjectId is { } code ? summaries.GetValueOrDefault(code)?.Code : null, row.ProjectId is { } name ? summaries.GetValueOrDefault(name)?.Name : null,
                row.OwnerEmployeeId is { } owner ? people.GetValueOrDefault(owner)?.DisplayName : null, row.Status, row.Hours, row.BillableHours, row.SubmittedDate, row.WorkflowInstanceId))],
            page,
            result.TotalCount);
    }

    public async Task ReopenAsync(Guid id, ReopenBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Timesheet timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Timesheet), id);
        List<TimesheetApproval> shares = await dbContext.TimesheetApprovals.Where(row => row.TimesheetId == id).ToListAsync(cancellationToken);
        List<TimesheetApproval> chosen = [.. shares.Where(row => row.Status == ApprovalStatus.Approved && (body.ApprovalId is null || row.Id == body.ApprovalId))];

        // Whoever reaches the person may reopen; an approver may reopen what they approved.
        bool canReach = await employees.IsInScopeAsync(timesheet.EmployeeId, Permissions.Timesheet.Approve, cancellationToken);
        if (!canReach && (chosen.Count == 0 || chosen.Any(row => row.OwnerEmployeeId != currentUser.EmployeeId)))
        {
            throw new NotFoundException(nameof(Timesheet), id);
        }

        DateOnly? lockedThrough = (await RulesAsync(cancellationToken)).LockedThrough;
        List<TimesheetEntry> entries = await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == id).ToListAsync(cancellationToken);

        // A week approved before approval went by project has no shares, and reopens whole.
        if (shares.Count == 0)
        {
            timesheet.Reopen(body.Reason, lockedThrough);
            publisher.Publish(new TimesheetReopenedIntegrationEvent(timesheet.Id, timesheet.EmployeeId, timesheet.WeekStart, TaskHoursOf(entries)));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (chosen.Count == 0)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only approved hours are reopened; a draft can simply be edited.");
        }

        timesheet.RecordCorrection(body.Reason, lockedThrough);
        foreach (TimesheetApproval share in chosen)
        {
            share.Reopen();
            publisher.Publish(new TimesheetReopenedIntegrationEvent(timesheet.Id, timesheet.EmployeeId, timesheet.WeekStart, TaskHoursOf(entries.Where(entry => entry.ApprovalId == share.Id))));
        }

        Settle(timesheet, shares, entries);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>People in reach with a project allocation that week and no submitted or approved timesheet for it.</summary>
    public async Task<IReadOnlyList<MissingRow>> MissingAsync(DateOnly week, CancellationToken cancellationToken)
    {
        DateOnly weekStart = Timesheet.WeekOf(week);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Timesheet.View, cancellationToken);
        List<(Guid EmployeeId, TimesheetStatus? Status)> missing = await MissingForWeekAsync(weekStart, scope, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(missing.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return [.. missing
            .Where(row => people.ContainsKey(row.EmployeeId))
            .Select(row => new MissingRow(row.EmployeeId, people[row.EmployeeId].DisplayName, people[row.EmployeeId].EmployeeCode, people[row.EmployeeId].Designation, row.Status))
            .OrderBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
    }

    /// <summary>Hours booked to a project, by person, task and classification - for whoever can see the project.</summary>
    public async Task<IReadOnlyList<ProjectHoursRow>> ProjectHoursAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!await projectPermissions.CanAsync(projectId, Permissions.Project.View, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        List<LoggedHours> rows = await BookedHours.Query(
                dbContext, dbContext.TimesheetEntries.AsNoTracking().Where(entry => entry.ProjectId == projectId && entry.Date >= from && entry.Date <= to), approvedOnly: false)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(rows.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. rows
            .GroupBy(row => new { row.EmployeeId, row.TaskId, row.Category })
            .Select(group => new ProjectHoursRow(
                group.Key.EmployeeId, names.GetValueOrDefault(group.Key.EmployeeId), group.Key.TaskId, group.Key.Category,
                group.Sum(row => row.Hours), group.Where(row => row.Approved).Sum(row => row.Hours)))
            .OrderBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
    }

    // ----- Settings -----------------------------------------------------------------------------

    public async Task<TimesheetRules> RulesAsync(CancellationToken cancellationToken)
        => (await dbContext.TimesheetSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToRules() ?? TimesheetRules.Default;

    public async Task<TimesheetRules> SaveRulesAsync(TimesheetRules rules, CancellationToken cancellationToken)
    {
        TimesheetSettings settings = await SettingsAsync(cancellationToken);
        settings.Update(rules);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings.ToRules();
    }

    public async Task<TimesheetRules> LockAsync(DateOnly through, CancellationToken cancellationToken)
    {
        TimesheetSettings settings = await SettingsAsync(cancellationToken);
        settings.LockThrough(through);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings.ToRules();
    }

    // ----- Workflow and jobs --------------------------------------------------------------------

    public async Task ApplyDecisionAsync(Guid subjectId, bool? approved, CancellationToken cancellationToken)
    {
        TimesheetApproval? share = await dbContext.TimesheetApprovals.FirstOrDefaultAsync(row => row.Id == subjectId, cancellationToken);
        if (share is not null)
        {
            await ApplyShareDecisionAsync(share, approved, cancellationToken);
            return;
        }

        // A request raised before approval went by project is about the whole week.
        Guid timesheetId = subjectId;
        Timesheet? timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.Id == timesheetId, cancellationToken);
        if (timesheet?.Status != TimesheetStatus.Submitted)
        {
            return;
        }

        switch (approved)
        {
            case true:
                timesheet.Approve(Now);
                List<TimesheetEntry> whole = await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheetId).ToListAsync(cancellationToken);
                publisher.Publish(new TimesheetHoursApprovedIntegrationEvent(timesheet.Id, timesheet.EmployeeId, timesheet.WeekStart, null, TaskHoursOf(whole)));
                PublishApproved(timesheet, whole);
                break;
            case false:
                timesheet.Reject(Now);
                break;
            default:
                timesheet.ReturnToDraft();
                break;
        }
    }

    private async Task ApplyShareDecisionAsync(TimesheetApproval share, bool? approved, CancellationToken cancellationToken)
    {
        if (share.Status != ApprovalStatus.Submitted)
        {
            return;
        }

        Timesheet timesheet = await dbContext.Timesheets.FirstAsync(row => row.Id == share.TimesheetId, cancellationToken);
        List<TimesheetEntry> entries = await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        switch (approved)
        {
            case true:
                share.Approve(Now);
                PublishHoursApproved(share, entries.Where(entry => entry.ApprovalId == share.Id));
                break;
            case false:
                share.Reject(Now);
                break;
            default:
                share.Return();
                break;
        }

        List<TimesheetApproval> shares = await dbContext.TimesheetApprovals.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        Settle(timesheet, shares, entries);
    }

    /// <summary>Reminds everyone allocated last week who has not submitted it. Returns how many were reminded.</summary>
    public async Task<int> RemindMissingAsync(CancellationToken cancellationToken)
    {
        DateOnly weekStart = Timesheet.WeekOf(await clock.TodayAsync(cancellationToken)).AddDays(-7);
        List<(Guid EmployeeId, TimesheetStatus? Status)> missing = await MissingForWeekAsync(weekStart, scope: null, cancellationToken);
        if (missing.Count == 0)
        {
            return 0;
        }

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.TimesheetMissing,
                [.. missing.Select(row => new NotificationRecipient(row.EmployeeId))],
                new Dictionary<string, string> { ["weekStart"] = weekStart.ToString("d MMM yyyy", CultureInfo.InvariantCulture) },
                $"/me/timesheet?week={weekStart:yyyy-MM-dd}"),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return missing.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("TIMESHEET_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private Timesheet Track(Timesheet timesheet)
    {
        dbContext.Timesheets.Add(timesheet);
        return timesheet;
    }

    private async Task<TimesheetSettings> SettingsAsync(CancellationToken cancellationToken)
    {
        TimesheetSettings? settings = await dbContext.TimesheetSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = TimesheetSettings.Defaults();
            dbContext.TimesheetSettings.Add(settings);
        }

        return settings;
    }

    private void PublishApproved(Timesheet timesheet, IReadOnlyList<TimesheetEntry> entries)
        => publisher.Publish(new TimesheetApprovedIntegrationEvent(timesheet.Id, timesheet.EmployeeId, timesheet.WeekStart, TaskHoursOf(entries)));

    private void PublishHoursApproved(TimesheetApproval share, IEnumerable<TimesheetEntry> entries)
        => publisher.Publish(new TimesheetHoursApprovedIntegrationEvent(share.TimesheetId, share.EmployeeId, share.WeekStart, share.ProjectId, TaskHoursOf(entries)));

    /// <summary>Which share of the week an hour belongs to: its project's, unless it is a default project or none, which share one approver.</summary>
    private static Guid? ShareOf(Guid? projectId, IReadOnlyDictionary<Guid, ProjectSummary> summaries)
        => projectId is { } id && summaries.TryGetValue(id, out ProjectSummary? project) && !project.IsDefault ? id : null;

    private static bool SameHours(
        IEnumerable<(DateOnly Date, Guid? ProjectId, Guid? TaskId, HourCategory Category, decimal Hours)> first,
        IEnumerable<(DateOnly Date, Guid? ProjectId, Guid? TaskId, HourCategory Category, decimal Hours)> second)
    {
        static Dictionary<(DateOnly, Guid?, Guid?, HourCategory), decimal> Totals(IEnumerable<(DateOnly Date, Guid? ProjectId, Guid? TaskId, HourCategory Category, decimal Hours)> rows)
            => rows.GroupBy(row => (row.Date, row.ProjectId, row.TaskId, row.Category)).ToDictionary(group => group.Key, group => group.Sum(row => row.Hours));

        Dictionary<(DateOnly, Guid?, Guid?, HourCategory), decimal> left = Totals(first);
        Dictionary<(DateOnly, Guid?, Guid?, HourCategory), decimal> right = Totals(second);
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out decimal hours) && hours == pair.Value);
    }

    /// <summary>
    /// Who a share goes to: its project's manager, otherwise the person's own manager, then their
    /// department's head. Nobody approves their own hours, so a manager's own project goes up the same way.
    /// </summary>
    private async Task<Guid?> OwnerAsync(Guid? share, EmployeeSummary? person, IReadOnlyDictionary<Guid, ProjectSummary> summaries, CancellationToken cancellationToken)
    {
        Guid? me = person?.Id;
        if (share is { } id && summaries.GetValueOrDefault(id)?.ProjectManagerEmployeeId is { } manager && manager != me)
        {
            return manager;
        }

        if (person?.ManagerEmployeeId is { } reportsTo && reportsTo != me)
        {
            return reportsTo;
        }

        return person?.DepartmentId is { } department && await org.GetDepartmentHeadAsync(department, cancellationToken) is { } head && head != me ? head : null;
    }

    /// <summary>The week takes the status its shares add up to, and announces itself when the last one is approved.</summary>
    private void Settle(Timesheet timesheet, IReadOnlyCollection<TimesheetApproval> shares, IReadOnlyList<TimesheetEntry> entries)
    {
        bool wasApproved = timesheet.Status == TimesheetStatus.Approved;
        HashSet<Guid> known = [.. shares.Select(share => share.Id)];
        TimesheetStatus status = TimesheetApprovals.WeekStatus([.. shares.Select(share => share.Status)], entries.Any(entry => entry.ApprovalId is not { } id || !known.Contains(id)));
        timesheet.Settle(status, shares.FirstOrDefault(share => share.Status == ApprovalStatus.Submitted)?.WorkflowInstanceId, Now);
        if (status == TimesheetStatus.Approved && !wasApproved)
        {
            PublishApproved(timesheet, entries);
        }
    }

    private static List<TaskHours> TaskHoursOf(IEnumerable<TimesheetEntry> entries)
        => [.. entries.Where(entry => entry.TaskId is not null).GroupBy(entry => entry.TaskId!.Value).Select(group => new TaskHours(group.Key, group.Sum(entry => entry.Hours)))];

    private async Task<List<(Guid EmployeeId, TimesheetStatus? Status)>> MissingForWeekAsync(DateOnly weekStart, IReadOnlyCollection<Guid>? scope, CancellationToken cancellationToken)
    {
        DateOnly weekEnd = weekStart.AddDays(6);
        HashSet<Guid> allocated = [.. (await projects.AllocationsAsync(scope, weekStart, weekEnd, cancellationToken)).Select(allocation => allocation.EmployeeId)];
        if ((await RulesAsync(cancellationToken)).RequireFromEveryone)
        {
            IEnumerable<Guid> everyone = await employees.ListActiveIdsAsync(null, null, cancellationToken);
            allocated.UnionWith(scope is null ? everyone : everyone.Where(scope.Contains));
        }

        if (allocated.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, TimesheetStatus> sheets = await dbContext.Timesheets.AsNoTracking()
            .Where(row => row.WeekStart == weekStart && allocated.Contains(row.EmployeeId))
            .ToDictionaryAsync(row => row.EmployeeId, row => row.Status, cancellationToken);

        List<Guid> candidates = [.. allocated.Where(id => !sheets.TryGetValue(id, out TimesheetStatus status) || status is TimesheetStatus.Draft or TimesheetStatus.Rejected)];
        HashSet<Guid> active = [.. (await employees.GetManyAsync(candidates, cancellationToken))
            .Where(person => person.Status != "Exited" && (person.JoiningDate is null || person.JoiningDate <= weekEnd))
            .Select(person => person.Id)];
        return [.. candidates.Where(active.Contains).Select(id => (id, sheets.TryGetValue(id, out TimesheetStatus status) ? (TimesheetStatus?)status : null))];
    }

    private Task<IReadOnlyList<DayFacts>> DaysAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken)
        => FactsAsync(employeeId, weekStart, weekStart.AddDays(6), cancellationToken);

    private static decimal LeaveHours(DayFacts day, TimesheetRules rules) => day.LeaveFraction * rules.StandardHoursPerDay;

    private static decimal HolidayHours(DayFacts day, TimesheetRules rules) => day.IsWorkingDay && day.Holiday is not null ? rules.StandardHoursPerDay : 0;

    private async Task<IReadOnlyList<DayFacts>> FactsAsync(Guid employeeId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        Dictionary<DateOnly, ScheduledDay> schedule = (await shifts.GetAsync([employeeId], firstDay, lastDay, cancellationToken)).ToDictionary(day => day.Date);
        IReadOnlyList<HolidayEntry> holidays = (await leave.GetHolidaysAsync([employeeId], firstDay, lastDay, cancellationToken)).GetValueOrDefault(employeeId) ?? [];
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync([employeeId], firstDay, lastDay, cancellationToken);

        return [.. Enumerable.Range(0, lastDay.DayNumber - firstDay.DayNumber + 1).Select(offset =>
        {
            DateOnly date = firstDay.AddDays(offset);
            bool working = schedule.TryGetValue(date, out ScheduledDay? day) ? !day.IsWeeklyOff : date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            string? holiday = holidays.FirstOrDefault(entry => entry.Date == date && !entry.IsOptional)?.Name;
            decimal leaveFraction = Math.Min(1, leaveDays.Where(entry => entry.Date == date).Sum(entry => entry.IsHalfDay ? 0.5m : 1m));
            return new DayFacts(date, working, holiday, working ? leaveFraction : 0);
        })];
    }

    private async Task<TimesheetView> ViewAsync(Guid employeeId, DateOnly weekStart, bool withTargets, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? onlyShares = null)
    {
        DateOnly weekEnd = weekStart.AddDays(6);
        TimesheetRules rules = await RulesAsync(cancellationToken);
        Timesheet? timesheet = await dbContext.Timesheets.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == employeeId && row.WeekStart == weekStart, cancellationToken);
        List<TimesheetEntry> entries = timesheet is null
            ? []
            : await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).OrderBy(row => row.Date).ToListAsync(cancellationToken);
        List<TimesheetApproval> shares = timesheet is null
            ? []
            : await dbContext.TimesheetApprovals.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        if (onlyShares is not null)
        {
            entries = [.. entries.Where(entry => entry.ApprovalId is { } id && onlyShares.Contains(id))];
            shares = [.. shares.Where(share => onlyShares.Contains(share.Id))];
        }


        IReadOnlyList<DayFacts> facts = await DaysAsync(employeeId, weekStart, cancellationToken);
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync([employeeId], weekStart, weekEnd, cancellationToken);
        Dictionary<DateOnly, AttendedDay> attended = (await attendance.DaysAsync(employeeId, weekStart, weekEnd, cancellationToken)).ToDictionary(day => day.Date);
        IReadOnlyList<TimesheetTarget> targets = withTargets ? await projects.TimesheetTargetsAsync(employeeId, weekStart, weekEnd, cancellationToken) : [];

        // Names for every project and task booked, including ones no longer offered as targets.
        Dictionary<Guid, string> projectCodes = targets.ToDictionary(target => target.ProjectId, target => target.ProjectCode);
        Dictionary<Guid, string> taskCodes = targets.SelectMany(target => target.Tasks).ToDictionary(task => task.TaskId, task => task.Code);
        List<Guid> unknownProjects = [.. entries.Select(entry => entry.ProjectId).OfType<Guid>().Where(id => !projectCodes.ContainsKey(id)).Distinct()];
        foreach ((Guid id, ProjectSummary project) in await projects.GetManyAsync(unknownProjects, cancellationToken))
        {
            projectCodes[id] = project.Code;
        }

        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        IEnumerable<(DateOnly Date, decimal Hours)> hours = entries.Select(entry => (entry.Date, entry.Hours));
        IReadOnlyDictionary<Guid, ProjectSummary> shareProjects = await projects.GetManyAsync(shares.Select(share => share.ProjectId).OfType<Guid>(), cancellationToken);
        Dictionary<Guid, string> owners = (await employees.GetManyAsync(shares.Select(share => share.OwnerEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(owner => owner.Id, owner => owner.DisplayName);

        var header = new TimesheetHeader(
            timesheet?.Id, employeeId, person?.DisplayName, weekStart, weekEnd, timesheet?.Status ?? TimesheetStatus.Draft, timesheet?.TotalHours ?? 0,
            timesheet?.BillableHours ?? 0, timesheet?.SubmittedDate, timesheet?.DecidedDate, timesheet?.IsLate ?? false, timesheet?.WorkflowInstanceId,
            timesheet?.Comment, timesheet?.CorrectionCount ?? 0, timesheet?.LastCorrectionReason, rules.LockedThrough is { } locked && weekStart <= locked);

        return new TimesheetView(
            header,
            [.. entries.Select(entry => new EntryItem(
                entry.Id, entry.Date, entry.ProjectId, entry.ProjectId is { } project ? projectCodes.GetValueOrDefault(project) : null, entry.TaskId,
                entry.TaskId is { } task ? taskCodes.GetValueOrDefault(task) : null, entry.Category, entry.Hours, entry.Activity, entry.Description))],
            [.. facts.Select(day => new DayItem(
                day.Date, day.IsWorkingDay, day.Holiday, leaveDays.FirstOrDefault(entry => entry.Date == day.Date)?.LeaveTypeName,
                entries.Where(entry => entry.Date == day.Date).Sum(entry => entry.Hours), LeaveHours(day, rules), HolidayHours(day, rules),
                attended.GetValueOrDefault(day.Date)?.Status, attended.GetValueOrDefault(day.Date)?.WorkedMinutes))],
            targets,
            TimesheetChecks.Check(rules, facts, hours),
            entries.GroupBy(entry => entry.Category.ToString()).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours)),
            TimesheetChecks.Overtime(rules, facts, hours),
            rules,
            [.. shares.Select(share => new ApprovalItem(
                share.Id, share.ProjectId, share.ProjectId is { } code ? shareProjects.GetValueOrDefault(code)?.Code : null, share.ProjectId is { } name ? shareProjects.GetValueOrDefault(name)?.Name : null,
                share.OwnerEmployeeId, share.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null, share.Status, share.Hours, share.BillableHours, share.WorkflowInstanceId, share.DecidedDate))
                .OrderBy(share => share.ProjectCode, StringComparer.Ordinal)]);
    }
}

/// <summary>
/// Which hours count. Hours go by their share of the week, so one project's approval does not wait for
/// another's. Hours from before approval went by project, which have no share, go by their week.
/// </summary>
internal static class BookedHours
{
    public static IQueryable<LoggedHours> Query(AppDbContext dbContext, IQueryable<TimesheetEntry> entries, bool approvedOnly)
        => from entry in entries
           join sheet in dbContext.Timesheets.AsNoTracking() on entry.TimesheetId equals sheet.Id
           join share in dbContext.TimesheetApprovals.AsNoTracking() on entry.ApprovalId equals (Guid?)share.Id into matched
           from share in matched.DefaultIfEmpty()
           let approved = share != null ? share.Status == ApprovalStatus.Approved : sheet.Status == TimesheetStatus.Approved
           let submitted = share != null ? share.Status == ApprovalStatus.Submitted : sheet.Status == TimesheetStatus.Submitted
           where approved || (!approvedOnly && submitted)
           select new LoggedHours(entry.EmployeeId, entry.ProjectId, entry.TaskId, entry.Date, entry.Category, entry.Hours, approved);
}

/// <summary>Booked hours for utilization, project financials and payroll.</summary>
internal sealed class TimesheetLedger(AppDbContext dbContext, IShiftSchedule shifts, ILeaveCalendar leave) : ITimesheetLedger
{
    public async Task<IReadOnlyList<LoggedHours>> ProjectHoursAsync(IEnumerable<Guid>? projectIds, DateOnly firstDay, DateOnly lastDay, bool approvedOnly, CancellationToken cancellationToken = default)
    {
        IQueryable<TimesheetEntry> entries = dbContext.TimesheetEntries.AsNoTracking().Where(entry => entry.ProjectId != null && entry.Date >= firstDay && entry.Date <= lastDay);
        if (projectIds is not null)
        {
            List<Guid> ids = [.. projectIds.Distinct()];
            entries = entries.Where(entry => ids.Contains(entry.ProjectId!.Value));
        }

        return await BookedHours.Query(dbContext, entries, approvedOnly).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> OvertimeAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        // Overtime is judged on a whole day, so it waits until every hour of the week is approved: pay must not depend on which project answered first.
        IQueryable<TimesheetEntry> entries = dbContext.TimesheetEntries.AsNoTracking().Where(entry => entry.Date >= firstDay && entry.Date <= lastDay);
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds.Distinct()];
            entries = entries.Where(entry => ids.Contains(entry.EmployeeId));
        }

        List<LoggedHours> hours = await entries
            .Join(dbContext.Timesheets.AsNoTracking().Where(sheet => sheet.Status == TimesheetStatus.Approved), entry => entry.TimesheetId, sheet => sheet.Id, (entry, sheet) => new LoggedHours(
                entry.EmployeeId, entry.ProjectId, entry.TaskId, entry.Date, entry.Category, entry.Hours, true))
            .ToListAsync(cancellationToken);
        List<Guid> people = [.. hours.Select(row => row.EmployeeId).Distinct()];
        if (people.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        TimesheetRules rules = (await dbContext.TimesheetSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToRules() ?? TimesheetRules.Default;
        ILookup<Guid, ScheduledDay> schedule = (await shifts.GetAsync(people, firstDay, lastDay, cancellationToken)).ToLookup(day => day.EmployeeId);
        IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = await leave.GetHolidaysAsync(people, firstDay, lastDay, cancellationToken);

        var overtime = new Dictionary<Guid, decimal>();
        foreach (IGrouping<Guid, LoggedHours> person in hours.GroupBy(row => row.EmployeeId))
        {
            Dictionary<DateOnly, ScheduledDay> days = schedule[person.Key].ToDictionary(day => day.Date);
            HashSet<DateOnly> offDays = [.. (holidays.GetValueOrDefault(person.Key) ?? []).Where(entry => !entry.IsOptional).Select(entry => entry.Date)];
            List<DayFacts> facts = [.. person.Select(row => row.Date).Distinct().Select(date => new DayFacts(
                date,
                days.TryGetValue(date, out ScheduledDay? day) ? !day.IsWeeklyOff : date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                offDays.Contains(date) ? "Holiday" : null,
                0))];
            decimal extra = TimesheetChecks.Overtime(rules, facts, person.Select(row => (row.Date, row.Hours)));
            if (extra > 0)
            {
                overtime[person.Key] = extra;
            }
        }

        return overtime;
    }

    public async Task<IReadOnlyList<LoggedHours>> HoursAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, bool approvedOnly, CancellationToken cancellationToken = default)
    {
        IQueryable<TimesheetEntry> entries = dbContext.TimesheetEntries.AsNoTracking().Where(entry => entry.Date >= firstDay && entry.Date <= lastDay);
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds.Distinct()];
            entries = entries.Where(entry => ids.Contains(entry.EmployeeId));
        }

        return await BookedHours.Query(dbContext, entries, approvedOnly).ToListAsync(cancellationToken);
    }
}
