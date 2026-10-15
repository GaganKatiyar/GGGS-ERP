using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Timesheets.Domain;
using Gggs.Modules.Timesheets.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Timesheets.Application;

public sealed record EntryItem(Guid Id, DateOnly Date, Guid? ProjectId, string? ProjectCode, Guid? TaskId, string? TaskCode, HourCategory Category, decimal Hours, string? Activity, string? Description);

public sealed record DayItem(DateOnly Date, bool IsWorkingDay, string? Holiday, string? Leave, decimal Hours);

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

public sealed record TimesheetView(
    TimesheetHeader Timesheet,
    IReadOnlyList<EntryItem> Entries,
    IReadOnlyList<DayItem> Days,
    IReadOnlyList<TimesheetTarget> Targets,
    IReadOnlyList<TimesheetIssue> Issues,
    IReadOnlyDictionary<string, decimal> ByCategory,
    decimal OvertimeHours,
    TimesheetRules Rules);

public sealed record EntryBody(DateOnly Date, Guid? ProjectId, Guid? TaskId, HourCategory Category, decimal Hours, string? Activity, string? Description);

public sealed record WeekBody(IReadOnlyList<EntryBody> Entries, string? Comment);

public sealed record SubmitOutcome(TimesheetStatus Status, Guid? WorkflowInstanceId, IReadOnlyList<TimesheetIssue> Issues);

public sealed record ReopenBody(string Reason);

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

/// <summary>
/// Timesheets (spec §70-71): a person's week against the projects they are on, checked against the
/// tenant's rules, submitted through approval, reopened for corrections, and locked by period.
/// </summary>
internal sealed class TimesheetService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IProjectDirectory projects,
    IProjectPermissions projectPermissions,
    IShiftSchedule shifts,
    ILeaveCalendar leave,
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
        ]);

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

        IReadOnlyDictionary<Guid, Guid> taskProjects = await projects.TaskProjectsAsync(body.Entries.Select(entry => entry.TaskId).OfType<Guid>(), cancellationToken);
        var onTeam = new Dictionary<(Guid, DateOnly), bool>();
        List<TimesheetEntry> entries = [];
        foreach (EntryBody entry in body.Entries.Where(entry => entry.Hours > 0))
        {
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

        List<TimesheetEntry> existing = await dbContext.TimesheetEntries.Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
        dbContext.TimesheetEntries.RemoveRange(existing);
        dbContext.TimesheetEntries.AddRange(entries);
        timesheet.Recalculate(entries, body.Comment);

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
        List<TimesheetEntry> entries = await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).ToListAsync(cancellationToken);
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
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = timesheet.Id,
                Title = $"Timesheet, week of {weekStart:d MMM yyyy} ({timesheet.TotalHours:0.##} h)",
                RequesterEmployeeId = me,
                Link = $"/team/timesheets?open={timesheet.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["totalHours"] = timesheet.TotalHours.ToString(CultureInfo.InvariantCulture),
                    ["billableHours"] = timesheet.BillableHours.ToString(CultureInfo.InvariantCulture),
                    ["overtimeHours"] = TimesheetChecks.Overtime(rules, days, entries.Select(entry => (entry.Date, entry.Hours))).ToString(CultureInfo.InvariantCulture),
                    ["late"] = late ? "true" : "false",
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            timesheet.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            timesheet.Approve(Now);
            PublishApproved(timesheet, entries);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SubmitOutcome(timesheet.Status, started.InstanceId, issues);
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
        timesheet.ReturnToDraft();
        await dbContext.SaveChangesAsync(cancellationToken);
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
        if (timesheet.EmployeeId != currentUser.EmployeeId && !await employees.IsInScopeAsync(timesheet.EmployeeId, Permissions.Timesheet.View, cancellationToken))
        {
            throw new NotFoundException(nameof(Timesheet), id);
        }

        return await ViewAsync(timesheet.EmployeeId, timesheet.WeekStart, withTargets: false, cancellationToken);
    }

    public async Task ReopenAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        Timesheet timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Timesheet), id);
        if (!await employees.IsInScopeAsync(timesheet.EmployeeId, Permissions.Timesheet.Approve, cancellationToken))
        {
            throw new NotFoundException(nameof(Timesheet), id);
        }

        timesheet.Reopen(reason, (await RulesAsync(cancellationToken)).LockedThrough);
        List<TimesheetEntry> entries = await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == id).ToListAsync(cancellationToken);
        publisher.Publish(new TimesheetReopenedIntegrationEvent(timesheet.Id, timesheet.EmployeeId, timesheet.WeekStart, TaskHoursOf(entries)));
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

        var rows = await dbContext.TimesheetEntries.AsNoTracking()
            .Where(entry => entry.ProjectId == projectId && entry.Date >= from && entry.Date <= to)
            .Join(
                dbContext.Timesheets.AsNoTracking().Where(sheet => sheet.Status == TimesheetStatus.Submitted || sheet.Status == TimesheetStatus.Approved),
                entry => entry.TimesheetId,
                sheet => sheet.Id,
                (entry, sheet) => new { entry.EmployeeId, entry.TaskId, entry.Category, entry.Hours, Approved = sheet.Status == TimesheetStatus.Approved })
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

    public async Task ApplyDecisionAsync(Guid timesheetId, bool? approved, CancellationToken cancellationToken)
    {
        Timesheet? timesheet = await dbContext.Timesheets.FirstOrDefaultAsync(row => row.Id == timesheetId, cancellationToken);
        if (timesheet?.Status != TimesheetStatus.Submitted)
        {
            return;
        }

        switch (approved)
        {
            case true:
                timesheet.Approve(Now);
                PublishApproved(timesheet, await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheetId).ToListAsync(cancellationToken));
                break;
            case false:
                timesheet.Reject(Now);
                break;
            default:
                timesheet.ReturnToDraft();
                break;
        }
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

    private static List<TaskHours> TaskHoursOf(IEnumerable<TimesheetEntry> entries)
        => [.. entries.Where(entry => entry.TaskId is not null).GroupBy(entry => entry.TaskId!.Value).Select(group => new TaskHours(group.Key, group.Sum(entry => entry.Hours)))];

    private async Task<List<(Guid EmployeeId, TimesheetStatus? Status)>> MissingForWeekAsync(DateOnly weekStart, IReadOnlyCollection<Guid>? scope, CancellationToken cancellationToken)
    {
        DateOnly weekEnd = weekStart.AddDays(6);
        HashSet<Guid> allocated = [.. (await projects.AllocationsAsync(scope, weekStart, weekEnd, cancellationToken)).Select(allocation => allocation.EmployeeId)];
        if (allocated.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, TimesheetStatus> sheets = await dbContext.Timesheets.AsNoTracking()
            .Where(row => row.WeekStart == weekStart && allocated.Contains(row.EmployeeId))
            .ToDictionaryAsync(row => row.EmployeeId, row => row.Status, cancellationToken);

        List<Guid> candidates = [.. allocated.Where(id => !sheets.TryGetValue(id, out TimesheetStatus status) || status is TimesheetStatus.Draft or TimesheetStatus.Rejected)];
        HashSet<Guid> active = [.. (await employees.GetManyAsync(candidates, cancellationToken)).Where(person => person.Status != "Exited").Select(person => person.Id)];
        return [.. candidates.Where(active.Contains).Select(id => (id, sheets.TryGetValue(id, out TimesheetStatus status) ? (TimesheetStatus?)status : null))];
    }

    private async Task<IReadOnlyList<DayFacts>> DaysAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        DateOnly weekEnd = weekStart.AddDays(6);
        Dictionary<DateOnly, ScheduledDay> schedule = (await shifts.GetAsync([employeeId], weekStart, weekEnd, cancellationToken)).ToDictionary(day => day.Date);
        IReadOnlyList<HolidayEntry> holidays = (await leave.GetHolidaysAsync([employeeId], weekStart, weekEnd, cancellationToken)).GetValueOrDefault(employeeId) ?? [];
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync([employeeId], weekStart, weekEnd, cancellationToken);

        return [.. Enumerable.Range(0, 7).Select(offset =>
        {
            DateOnly date = weekStart.AddDays(offset);
            bool working = schedule.TryGetValue(date, out ScheduledDay? day) ? !day.IsWeeklyOff : date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            string? holiday = holidays.FirstOrDefault(entry => entry.Date == date && !entry.IsOptional)?.Name;
            decimal leaveFraction = Math.Min(1, leaveDays.Where(entry => entry.Date == date).Sum(entry => entry.IsHalfDay ? 0.5m : 1m));
            return new DayFacts(date, working, holiday, working ? leaveFraction : 0);
        })];
    }

    private async Task<TimesheetView> ViewAsync(Guid employeeId, DateOnly weekStart, bool withTargets, CancellationToken cancellationToken)
    {
        DateOnly weekEnd = weekStart.AddDays(6);
        TimesheetRules rules = await RulesAsync(cancellationToken);
        Timesheet? timesheet = await dbContext.Timesheets.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == employeeId && row.WeekStart == weekStart, cancellationToken);
        List<TimesheetEntry> entries = timesheet is null
            ? []
            : await dbContext.TimesheetEntries.AsNoTracking().Where(row => row.TimesheetId == timesheet.Id).OrderBy(row => row.Date).ToListAsync(cancellationToken);

        IReadOnlyList<DayFacts> facts = await DaysAsync(employeeId, weekStart, cancellationToken);
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync([employeeId], weekStart, weekEnd, cancellationToken);
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
                entries.Where(entry => entry.Date == day.Date).Sum(entry => entry.Hours)))],
            targets,
            TimesheetChecks.Check(rules, facts, hours),
            entries.GroupBy(entry => entry.Category.ToString()).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours)),
            TimesheetChecks.Overtime(rules, facts, hours),
            rules);
    }
}

/// <summary>Booked hours for utilization.</summary>
internal sealed class TimesheetLedger(AppDbContext dbContext) : ITimesheetLedger
{
    public async Task<IReadOnlyList<LoggedHours>> HoursAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, bool approvedOnly, CancellationToken cancellationToken = default)
    {
        IQueryable<TimesheetEntry> entries = dbContext.TimesheetEntries.AsNoTracking().Where(entry => entry.Date >= firstDay && entry.Date <= lastDay);
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds.Distinct()];
            entries = entries.Where(entry => ids.Contains(entry.EmployeeId));
        }

        IQueryable<Timesheet> sheets = dbContext.Timesheets.AsNoTracking()
            .Where(sheet => sheet.Status == TimesheetStatus.Approved || (!approvedOnly && sheet.Status == TimesheetStatus.Submitted));

        return await entries
            .Join(sheets, entry => entry.TimesheetId, sheet => sheet.Id, (entry, sheet) => new LoggedHours(
                entry.EmployeeId, entry.ProjectId, entry.TaskId, entry.Date, entry.Category, entry.Hours, sheet.Status == TimesheetStatus.Approved))
            .ToListAsync(cancellationToken);
    }
}
