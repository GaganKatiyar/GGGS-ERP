using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record OffboardingTaskView(
    Guid Id,
    Guid CaseId,
    string Title,
    string? Description,
    OffboardingOwner Owner,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    DateOnly DueDate,
    bool Required,
    OffboardingTaskStatus Status,
    DateTime? CompletedAt,
    string? CompletedByName,
    string? Notes,
    bool Overdue,
    bool CanAct,
    string? EmployeeName = null);

public sealed record HandoverItemView(Guid Id, HandoverKind Kind, string Title, string? Detail, Guid? ToEmployeeId, string? ToName, HandoverStatus Status, DateTime? DoneAt, string? Outcome, bool NeedsRecipient);

public sealed record OffboardingView(
    Guid? SuccessorEmployeeId,
    string? SuccessorName,
    string? HandoverNote,
    DateTime? AccessDisabledAt,
    string? AccountStatus,
    IReadOnlyList<OffboardingTaskView> Tasks,
    IReadOnlyList<HandoverItemView> Handover);

public sealed record OffboardingTemplateView(Guid Id, string Name, string? Description, Guid? DepartmentId, Guid? LocationId, string? EmploymentTypeCode, bool IsDefault, bool IsActive, IReadOnlyList<OffboardingStep> Steps);

public sealed record OffboardingTemplateBody(string Name, string? Description, Guid? DepartmentId, Guid? LocationId, string? EmploymentTypeCode, bool IsDefault, bool IsActive, IReadOnlyList<OffboardingStep> Steps);

public sealed record HandoverBody(Guid? SuccessorEmployeeId, string? Note);

public sealed record HandoverActionBody(Guid? ToEmployeeId, bool Skip = false, string? Note = null);

public sealed record FinishTaskBody(bool Skipped, string? Notes);

/// <summary>
/// Offboarding for an approved separation (Separation v2 phase B): the checklist of actions around
/// a leaver, the handover of their people, project work and approvals to a successor, and switching
/// off their sign-in. Clearance stays the "no dues" record; this is the work.
/// </summary>
internal sealed class OffboardingService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeeSeparations separations,
    IProjectDirectory projects,
    IProjectStaffing staffing,
    IProjectTasks projectTasks,
    IWorkflowTasks workflowTasks,
    IUserAccounts accounts,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private static readonly OffboardingOwner[] TeamOwners = [OffboardingOwner.It, OffboardingOwner.Admin, OffboardingOwner.Finance];

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Lifecycle hooks (called by SeparationService; the caller saves) ----------------------

    /// <summary>On approval: lays out the checklist from the best-fitting template and looks for what needs handing over.</summary>
    public async Task StartAsync(SeparationCase separation, EmployeeSummary? person, CancellationToken cancellationToken)
    {
        if (await dbContext.OffboardingTasks.AnyAsync(row => row.CaseId == separation.Id, cancellationToken))
        {
            return;
        }

        OffboardingTemplate? template = await TemplateForAsync(person, cancellationToken);
        List<OffboardingTask> tasks = [];
        if (template is not null)
        {
            int order = 0;
            foreach (OffboardingStep step in template.Steps)
            {
                tasks.Add(OffboardingTask.From(separation.Id, step, separation.LastWorkingDay, AssigneeFor(step.Owner, separation, person), order++));
            }

            dbContext.OffboardingTasks.AddRange(tasks);
            Note(separation, "offboarding-started", "Offboarding started", $"{template.Name}: {tasks.Count} tasks.");
        }

        await RefreshHandoverAsync(separation, person, cancellationToken);
        await TellAssigneesAsync(separation, person?.DisplayName, tasks, cancellationToken);
    }

    /// <summary>Keeps open tasks on their offsets when the last working day moves.</summary>
    public async Task FollowLastDayAsync(SeparationCase separation, CancellationToken cancellationToken)
    {
        foreach (OffboardingTask task in await dbContext.OffboardingTasks.Where(row => row.CaseId == separation.Id && row.Status == OffboardingTaskStatus.Pending).ToListAsync(cancellationToken))
        {
            task.Follow(separation.LastWorkingDay);
        }
    }

    /// <summary>A withdrawn separation needs no offboarding: open tasks and handover items go.</summary>
    public async Task CancelAsync(SeparationCase separation, CancellationToken cancellationToken)
    {
        dbContext.OffboardingTasks.RemoveRange(await dbContext.OffboardingTasks.Where(row => row.CaseId == separation.Id && row.Status == OffboardingTaskStatus.Pending).ToListAsync(cancellationToken));
        dbContext.HandoverItems.RemoveRange(await dbContext.HandoverItems.Where(row => row.CaseId == separation.Id && row.Status == HandoverStatus.Open).ToListAsync(cancellationToken));
    }

    /// <summary>On exit: the leaver's sign-in is switched off (every session ends with it).</summary>
    public async Task DeprovisionAsync(SeparationCase separation, CancellationToken cancellationToken)
    {
        if (separation.AccessDisabledAt is null)
        {
            await DisableAsync(separation, "Access switched off on exit", cancellationToken);
        }
    }

    public async Task<OffboardingView> ViewAsync(SeparationCase separation, bool forHr, CancellationToken cancellationToken)
    {
        List<OffboardingTask> tasks = await dbContext.OffboardingTasks.AsNoTracking().Where(row => row.CaseId == separation.Id).OrderBy(row => row.DueDate).ThenBy(row => row.SortOrder).ToListAsync(cancellationToken);
        List<HandoverItem> items = forHr ? await dbContext.HandoverItems.AsNoTracking().Where(row => row.CaseId == separation.Id).OrderBy(row => row.Kind).ThenBy(row => row.Title).ToListAsync(cancellationToken) : [];
        Dictionary<Guid, string> names = await NamesAsync(
            [.. tasks.Select(row => row.AssigneeEmployeeId), .. tasks.Select(row => row.CompletedByEmployeeId), .. items.Select(row => row.ToEmployeeId), separation.SuccessorEmployeeId], cancellationToken);
        Access access = await AccessAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        UserAccountInfo? account = forHr ? await accounts.FindByEmployeeAsync(separation.EmployeeId, cancellationToken) : null;
        return new OffboardingView(
            separation.SuccessorEmployeeId,
            separation.SuccessorEmployeeId is { } successor ? names.GetValueOrDefault(successor) : null,
            separation.HandoverNote,
            separation.AccessDisabledAt,
            account?.Status,
            [.. tasks.Select(task => TaskView(task, names, separation, access, today, null))],
            [.. items.Select(item => new HandoverItemView(item.Id, item.Kind, item.Title, item.Detail, item.ToEmployeeId, item.ToEmployeeId is { } to ? names.GetValueOrDefault(to) : null, item.Status, item.DoneAt, item.Outcome, item.NeedsRecipient))]);
    }

    // ----- Tasks --------------------------------------------------------------------------------

    /// <summary>Open offboarding tasks waiting on the caller: theirs, their team queue's, or every unassigned one for offboarding managers.</summary>
    public async Task<IReadOnlyList<OffboardingTaskView>> MyTasksAsync(CancellationToken cancellationToken)
    {
        Access access = await AccessAsync(cancellationToken);
        Guid? me = currentUser.EmployeeId;
        List<(OffboardingTask Task, SeparationCase Case)> rows = [.. (await (
                from task in dbContext.OffboardingTasks.AsNoTracking()
                join separation in dbContext.SeparationCases.AsNoTracking() on task.CaseId equals separation.Id
                where task.Status == OffboardingTaskStatus.Pending && separation.Status == CaseStatus.Approved
                    && (task.AssigneeEmployeeId == me
                        || (task.Owner == OffboardingOwner.Employee && separation.EmployeeId == me)
                        || (task.AssigneeEmployeeId == null && (access.Manage || (access.Team && TeamOwners.Contains(task.Owner)))))
                orderby task.DueDate
                select new { task, separation })
            .Take(500)
            .ToListAsync(cancellationToken)).Select(row => (row.task, row.separation))];
        Dictionary<Guid, string> names = await NamesAsync([.. rows.Select(row => row.Task.AssigneeEmployeeId), .. rows.Select(row => (Guid?)row.Case.EmployeeId)], cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        return [.. rows.Select(row => TaskView(row.Task, names, row.Case, access, today, names.GetValueOrDefault(row.Case.EmployeeId)))];
    }

    public async Task<Guid> FinishTaskAsync(Guid taskId, FinishTaskBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        OffboardingTask task = await dbContext.OffboardingTasks.FirstOrDefaultAsync(row => row.Id == taskId, cancellationToken) ?? throw new NotFoundException(nameof(OffboardingTask), taskId);
        SeparationCase separation = await dbContext.SeparationCases.FirstAsync(row => row.Id == task.CaseId, cancellationToken);
        if (!CanAct(task, separation, await AccessAsync(cancellationToken)))
        {
            throw new ForbiddenException("OFFBOARDING_TASK_OWNER", "This task belongs to someone else.");
        }

        if (separation.Status != CaseStatus.Approved)
        {
            throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
        }

        task.Finish(body.Skipped, currentUser.EmployeeId, body.Notes, Now);
        Note(separation, "offboarding-task", body.Skipped ? $"Skipped: {task.Title}" : $"Done: {task.Title}", task.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return separation.Id;
    }

    // ----- Handover -----------------------------------------------------------------------------

    public async Task SetHandoverAsync(Guid caseId, HandoverBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = await ManagedAsync(caseId, cancellationToken);
        if (body.SuccessorEmployeeId is { } successor && !await employees.IsActiveAsync(successor, cancellationToken))
        {
            throw new DomainRuleException("SEPARATION_SUCCESSOR", "Choose a current employee as the successor.");
        }

        Guid? before = separation.SuccessorEmployeeId;
        separation.SetHandover(body.SuccessorEmployeeId, body.Note);
        if (before != separation.SuccessorEmployeeId)
        {
            List<OffboardingTask> theirs = await dbContext.OffboardingTasks
                .Where(row => row.CaseId == caseId && row.Owner == OffboardingOwner.Successor && row.Status == OffboardingTaskStatus.Pending).ToListAsync(cancellationToken);
            foreach (OffboardingTask task in theirs)
            {
                task.Assign(separation.SuccessorEmployeeId);
            }

            // Work and approvals follow the successor; reports stay with whoever they were going to
            // (the leaver's manager by default) unless HR moves them.
            Guid? manager = (await employees.GetAsync(separation.EmployeeId, cancellationToken))?.ManagerEmployeeId;
            foreach (HandoverItem item in await dbContext.HandoverItems.Where(row => row.CaseId == caseId && row.Status == HandoverStatus.Open).ToListAsync(cancellationToken))
            {
                bool defaulted = item.ToEmployeeId is null || item.ToEmployeeId == before || (item.Kind != HandoverKind.DirectReport && item.ToEmployeeId == manager);
                if (item.NeedsRecipient && defaulted)
                {
                    item.Target(separation.SuccessorEmployeeId);
                }
            }

            string? successorName = separation.SuccessorEmployeeId is { } id ? (await employees.GetAsync(id, cancellationToken))?.DisplayName : null;
            Note(separation, "successor", successorName is null ? "Successor cleared" : $"Successor: {successorName}", separation.HandoverNote);
            await TellAssigneesAsync(separation, (await employees.GetAsync(separation.EmployeeId, cancellationToken))?.DisplayName, theirs, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Looks again for reports, project seats, tasks and approvals still with the leaver.</summary>
    public async Task RefreshAsync(Guid caseId, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(caseId, cancellationToken);
        await RefreshHandoverAsync(separation, await employees.GetAsync(separation.EmployeeId, cancellationToken), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Carries a handover item out in the module that owns it - or skips it.</summary>
    public async Task<Guid> ActAsync(Guid itemId, HandoverActionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        HandoverItem item = await dbContext.HandoverItems.FirstOrDefaultAsync(row => row.Id == itemId, cancellationToken) ?? throw new NotFoundException(nameof(HandoverItem), itemId);
        SeparationCase separation = await ManagedAsync(item.CaseId, cancellationToken);
        if (!item.IsOpen)
        {
            throw new DomainRuleException("HANDOVER_STATE", "This handover item is already closed.");
        }

        if (body.Skip)
        {
            item.Close(skipped: true, currentUser.EmployeeId, body.Note, Now);
            Note(separation, "handover", $"Skipped: {item.Title}", body.Note);
            await dbContext.SaveChangesAsync(cancellationToken);
            return separation.Id;
        }

        Guid? to = body.ToEmployeeId ?? item.ToEmployeeId;
        string? toName = null;
        if (item.NeedsRecipient)
        {
            if (to is not { } recipient || recipient == separation.EmployeeId || !await employees.IsActiveAsync(recipient, cancellationToken))
            {
                throw new DomainRuleException("HANDOVER_RECIPIENT", "Choose a current employee, other than the leaver, to hand this to.");
            }

            toName = (await employees.GetAsync(recipient, cancellationToken))?.DisplayName;
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        string outcome;
        switch (item.Kind)
        {
            case HandoverKind.DirectReport:
                if (to == item.SourceId)
                {
                    throw new DomainRuleException("HANDOVER_RECIPIENT", "An employee cannot report to themselves.");
                }

                string leaver = (await employees.GetAsync(separation.EmployeeId, cancellationToken))?.DisplayName ?? "the previous manager";
                await separations.ChangeManagerAsync(item.SourceId!.Value, to!.Value, today, $"Handover: {leaver} is leaving", cancellationToken);
                outcome = $"Now reports to {toName}.";
                break;
            case HandoverKind.ProjectAllocation:
                DateOnly releaseOn = separation.LastWorkingDay < today ? today : separation.LastWorkingDay;
                await staffing.ReleaseAsync(item.SourceId!.Value, releaseOn, cancellationToken);
                outcome = $"Released from the project on {Format(releaseOn)}.";
                break;
            case HandoverKind.ProjectTasks:
                outcome = $"{await projectTasks.ReassignOpenAsync(separation.EmployeeId, to!.Value, cancellationToken)} task(s) moved to {toName}.";
                break;
            default:
                outcome = $"{await workflowTasks.ReassignAllAsync(separation.EmployeeId, to!.Value, "Handover: the approver is leaving", cancellationToken)} approval(s) moved to {toName}.";
                break;
        }

        item.Target(to);
        item.Close(skipped: false, currentUser.EmployeeId, body.Note is null ? outcome : $"{outcome} {body.Note}", Now);
        Note(separation, "handover", item.Title, outcome);
        await dbContext.SaveChangesAsync(cancellationToken);
        return separation.Id;
    }

    /// <summary>Switches the leaver's sign-in off now - for a dismissal, or when they leave early.</summary>
    public async Task DisableAccessNowAsync(Guid caseId, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(caseId, cancellationToken);
        if (separation.EmployeeId == currentUser.EmployeeId)
        {
            throw new DomainRuleException("CANNOT_DISABLE_SELF", "You cannot switch off your own access.");
        }

        if (!await DisableAsync(separation, "Access switched off by HR", cancellationToken))
        {
            throw new DomainRuleException("SEPARATION_NO_ACCOUNT", "This employee has no sign-in account.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Templates ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<OffboardingTemplateView>> TemplatesAsync(CancellationToken cancellationToken)
    {
        if (!await dbContext.OffboardingTemplates.AnyAsync(cancellationToken))
        {
            dbContext.OffboardingTemplates.Add(OffboardingTemplate.Standard());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return [.. (await dbContext.OffboardingTemplates.AsNoTracking().OrderByDescending(row => row.IsDefault).ThenBy(row => row.Name).ToListAsync(cancellationToken)).Select(TemplateView)];
    }

    public async Task<OffboardingTemplateView> SaveTemplateAsync(Guid? id, OffboardingTemplateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var details = new OffboardingTemplateDetails(body.Name, body.Description, body.DepartmentId, body.LocationId, body.EmploymentTypeCode, body.IsDefault, body.Steps);
        OffboardingTemplate template;
        if (id is { } existing)
        {
            template = await dbContext.OffboardingTemplates.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(OffboardingTemplate), existing);
            template.Update(details, body.IsActive);
        }
        else
        {
            template = OffboardingTemplate.Create(details);
            if (!body.IsActive)
            {
                template.Update(details, active: false);
            }

            dbContext.OffboardingTemplates.Add(template);
        }

        if (template.IsDefault)
        {
            foreach (OffboardingTemplate other in await dbContext.OffboardingTemplates.Where(row => row.IsDefault && row.Id != template.Id).ToListAsync(cancellationToken))
            {
                other.ClearDefault();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TemplateView(template);
    }

    public async Task RemoveTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        OffboardingTemplate template = await dbContext.OffboardingTemplates.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(OffboardingTemplate), id);
        dbContext.OffboardingTemplates.Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Reminders ----------------------------------------------------------------------------

    /// <summary>Reminds owners of overdue offboarding tasks, every three days. Returns the reminders sent.</summary>
    public async Task<int> RemindAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<(OffboardingTask Task, Guid EmployeeId)> rows = [.. (await (
                from task in dbContext.OffboardingTasks
                join separation in dbContext.SeparationCases on task.CaseId equals separation.Id
                where task.Status == OffboardingTaskStatus.Pending && task.DueDate < today && task.AssigneeEmployeeId != null && separation.Status == CaseStatus.Approved
                select new { task, separation.EmployeeId })
            .ToListAsync(cancellationToken)).Select(row => (row.task, row.EmployeeId))];
        List<(OffboardingTask Task, Guid EmployeeId)> due = [.. rows.Where(row => row.Task.NeedsReminder(today))];
        Dictionary<Guid, string> leavers = await NamesAsync([.. due.Select(row => (Guid?)row.EmployeeId)], cancellationToken);
        foreach ((OffboardingTask task, Guid employeeId) in due)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.OffboardingTaskOverdue,
                    [new NotificationRecipient(task.AssigneeEmployeeId!.Value)],
                    new Dictionary<string, string>
                    {
                        ["taskTitle"] = task.Title,
                        ["employeeName"] = leavers.GetValueOrDefault(employeeId) ?? "a leaver",
                        ["dueDate"] = Format(task.DueDate),
                    },
                    task.AssigneeEmployeeId == employeeId ? "/me/separation" : "/separation/offboarding"),
                cancellationToken);
            task.Reminded(today);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private sealed record Access(bool Manage, bool Team);

    private static string Format(DateOnly day) => day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static OffboardingTemplateView TemplateView(OffboardingTemplate row)
        => new(row.Id, row.Name, row.Description, row.DepartmentId, row.LocationId, row.EmploymentTypeCode, row.IsDefault, row.IsActive, row.Steps);

    private static Guid? AssigneeFor(OffboardingOwner owner, SeparationCase separation, EmployeeSummary? person) => owner switch
    {
        OffboardingOwner.Manager => person?.ManagerEmployeeId,
        OffboardingOwner.Hr => person?.HrbpEmployeeId,
        OffboardingOwner.Employee => separation.Type == SeparationType.Death ? null : separation.EmployeeId,
        OffboardingOwner.Successor => separation.SuccessorEmployeeId,
        _ => null,
    };

    private async Task<Access> AccessAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        return new Access(granted.Has(Permissions.Separation.OffboardingManage), granted.Has(Permissions.Separation.ClearanceManage));
    }

    private bool CanAct(OffboardingTask task, SeparationCase separation, Access access)
    {
        Guid? me = currentUser.EmployeeId;
        return access.Manage
            || (me is not null && task.AssigneeEmployeeId == me)
            || (task.Owner == OffboardingOwner.Employee && separation.EmployeeId == me)
            || (task.AssigneeEmployeeId is null && access.Team && TeamOwners.Contains(task.Owner));
    }

    private OffboardingTaskView TaskView(OffboardingTask task, Dictionary<Guid, string> names, SeparationCase separation, Access access, DateOnly today, string? employeeName)
        => new(task.Id, task.CaseId, task.Title, task.Description, task.Owner, task.AssigneeEmployeeId, task.AssigneeEmployeeId is { } assignee ? names.GetValueOrDefault(assignee) : null,
            task.DueDate, task.Required, task.Status, task.CompletedAt, task.CompletedByEmployeeId is { } by ? names.GetValueOrDefault(by) : null, task.Notes,
            task.IsOverdue(today), task.IsOpen && separation.Status == CaseStatus.Approved && CanAct(task, separation, access), employeeName);

    private async Task<OffboardingTemplate?> TemplateForAsync(EmployeeSummary? person, CancellationToken cancellationToken)
    {
        List<OffboardingTemplate> templates = await dbContext.OffboardingTemplates.ToListAsync(cancellationToken);
        if (templates.Count == 0)
        {
            OffboardingTemplate standard = OffboardingTemplate.Standard();
            dbContext.OffboardingTemplates.Add(standard);
            return standard;
        }

        return templates
            .Select(template => (Template: template, Fit: template.Fit(person?.DepartmentId, person?.LocationId, person?.EmploymentTypeCode)))
            .Where(candidate => candidate.Fit >= 0)
            .OrderByDescending(candidate => candidate.Fit)
            .ThenByDescending(candidate => candidate.Template.IsDefault)
            .Select(candidate => candidate.Template)
            .FirstOrDefault();
    }

    private async Task RefreshHandoverAsync(SeparationCase separation, EmployeeSummary? person, CancellationToken cancellationToken)
    {
        List<HandoverItem> existing = await dbContext.HandoverItems.Where(row => row.CaseId == separation.Id).ToListAsync(cancellationToken);
        HashSet<(HandoverKind, Guid)> known = [.. existing.Where(row => row.SourceId != null).Select(row => (row.Kind, row.SourceId!.Value))];
        Guid? receiver = separation.SuccessorEmployeeId ?? person?.ManagerEmployeeId;

        IReadOnlyList<Guid> reports = await separations.DirectReportsAsync(separation.EmployeeId, cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync([.. reports.Select(id => (Guid?)id)], cancellationToken);
        foreach (Guid report in reports.Where(id => !known.Contains((HandoverKind.DirectReport, id))))
        {
            dbContext.HandoverItems.Add(HandoverItem.Raise(separation.Id, HandoverKind.DirectReport, report, $"Move {names.GetValueOrDefault(report) ?? "a direct report"} to a new manager", null, receiver));
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly from = today < separation.LastWorkingDay ? today : separation.LastWorkingDay;
        IEnumerable<ProjectAllocation> seats = (await projects.AllocationsAsync([separation.EmployeeId], from, separation.LastWorkingDay, cancellationToken))
            .Where(seat => seat.IsActive && seat.StartDate <= separation.LastWorkingDay && (seat.EndDate is null || seat.EndDate > separation.LastWorkingDay));
        foreach (ProjectAllocation seat in seats.Where(seat => !known.Contains((HandoverKind.ProjectAllocation, seat.MemberId))))
        {
            dbContext.HandoverItems.Add(HandoverItem.Raise(separation.Id, HandoverKind.ProjectAllocation, seat.MemberId,
                $"Release from {seat.ProjectCode} {seat.ProjectName} ({seat.RoleName}, {seat.AllocationPercent}%)",
                $"The seat ends on the last working day, {Format(separation.LastWorkingDay)}. Staff the replacement from the project team.", null));
        }

        IReadOnlyList<OpenProjectTask> open = await projectTasks.OpenForAssigneeAsync(separation.EmployeeId, cancellationToken);
        Aggregate(existing, separation, HandoverKind.ProjectTasks, open.Count, $"Hand over {open.Count} open project task(s)",
            string.Join("; ", open.Take(10).Select(task => $"{task.Code} {task.Title}")) + (open.Count > 10 ? $"; and {open.Count - 10} more" : string.Empty), separation.SuccessorEmployeeId ?? receiver);

        int approvals = await workflowTasks.PendingCountAsync(separation.EmployeeId, cancellationToken);
        Aggregate(existing, separation, HandoverKind.Approvals, approvals, $"Reassign {approvals} pending approval(s)", "Approvals waiting in the leaver's inbox.", receiver);
    }

    private void Aggregate(List<HandoverItem> existing, SeparationCase separation, HandoverKind kind, int count, string title, string detail, Guid? to)
    {
        HandoverItem? open = existing.FirstOrDefault(row => row.Kind == kind && row.IsOpen);
        if (open is not null)
        {
            if (count == 0)
            {
                open.Close(skipped: false, null, "Nothing left to hand over.", Now);
            }
            else
            {
                open.Refresh(title, detail);
            }
        }
        else if (count > 0)
        {
            dbContext.HandoverItems.Add(HandoverItem.Raise(separation.Id, kind, null, title, detail, to));
        }
    }

    private async Task<bool> DisableAsync(SeparationCase separation, string title, CancellationToken cancellationToken)
    {
        UserAccountInfo? account = await accounts.FindByEmployeeAsync(separation.EmployeeId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        int disabled = await accounts.DisableForEmployeeAsync(separation.EmployeeId, cancellationToken);
        separation.AccessDisabled(Now);
        Note(separation, "access-disabled", title, disabled > 0 ? $"Sign-in for {account.Email} disabled; open sessions ended." : $"{account.Email} was already disabled.");
        return true;
    }

    private async Task TellAssigneesAsync(SeparationCase separation, string? leaverName, IReadOnlyList<OffboardingTask> tasks, CancellationToken cancellationToken)
    {
        foreach (IGrouping<Guid, OffboardingTask> group in tasks.Where(task => task.AssigneeEmployeeId != null).GroupBy(task => task.AssigneeEmployeeId!.Value))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.OffboardingTaskAssigned,
                    [new NotificationRecipient(group.Key)],
                    new Dictionary<string, string>
                    {
                        ["employeeName"] = group.Key == separation.EmployeeId ? "You" : leaverName ?? "A colleague",
                        ["tasks"] = string.Join("; ", group.Select(task => task.Title)),
                        ["lastDay"] = Format(separation.LastWorkingDay),
                    },
                    group.Key == separation.EmployeeId ? "/me/separation" : "/separation/offboarding"),
                cancellationToken);
        }
    }

    private async Task<SeparationCase> ManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(SeparationCase), id);
        if (!await employees.IsInScopeAsync(separation.EmployeeId, Permissions.Separation.OffboardingManage, cancellationToken))
        {
            throw new NotFoundException(nameof(SeparationCase), id);
        }

        return separation.Status == CaseStatus.Approved ? separation : throw new DomainRuleException("SEPARATION_NOT_APPROVED", "Offboarding is for an approved separation that has not yet completed.");
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.Where(id => id != null).Select(id => id!.Value).Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private void Note(SeparationCase separation, string kind, string title, string? note)
        => dbContext.CaseEvents.Add(CaseEvent.Record(separation.Id, kind, title, note, currentUser.EmployeeId, Now));
}
