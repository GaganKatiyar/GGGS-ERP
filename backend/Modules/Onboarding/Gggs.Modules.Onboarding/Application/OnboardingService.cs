using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Onboarding.Domain;
using Gggs.Modules.Onboarding.Infrastructure;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Onboarding.Application;

public sealed record TemplateItem(
    Guid Id,
    string Name,
    string? Description,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string? EmploymentTypeCode,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<TemplateTask> Tasks);

public sealed record TemplateBody(TemplateDetails Details, bool IsActive = true);

public sealed record TaskItem(
    Guid Id,
    Guid CaseId,
    string Title,
    string? Description,
    TaskCategory Category,
    AssigneeRole AssigneeRole,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    DateOnly DueDate,
    bool Required,
    OnboardingTaskStatus Status,
    DateTime? CompletedDate,
    string? CompletedByName,
    string? Notes,
    bool Overdue,
    bool CanAct);

public sealed record CaseItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string? Designation,
    string? DepartmentName,
    string TemplateName,
    DateOnly JoiningDate,
    Guid? ManagerEmployeeId,
    string? ManagerName,
    Guid? BuddyEmployeeId,
    string? BuddyName,
    CaseStatus Status,
    DateOnly? CompletedOn,
    int Tasks,
    int Done,
    int Overdue,
    int ProgressPercent);

public sealed record CaseView(CaseItem Case, IReadOnlyList<TaskItem> Tasks, bool CanManage);

public sealed record CaseQuery
{
    public CaseStatus? Status { get; init; }

    public bool? Open { get; init; }

    public Guid? EmployeeId { get; init; }
}

public sealed record StartBody(Guid EmployeeId, Guid? TemplateId, Guid? BuddyEmployeeId);

public sealed record BuddyBody(Guid? BuddyEmployeeId);

public sealed record FinishBody(bool Skipped, string? Notes);

public sealed record TaskChangeBody(Guid? AssigneeEmployeeId, DateOnly? DueDate);

public sealed record OwnersBody(Guid? HrOwnerEmployeeId, Guid? ItOwnerEmployeeId, Guid? AdminOwnerEmployeeId);

public sealed record OwnersView(Guid? HrOwnerEmployeeId, string? HrOwnerName, Guid? ItOwnerEmployeeId, string? ItOwnerName, Guid? AdminOwnerEmployeeId, string? AdminOwnerName);

public sealed record OnboardingDashboard(int Open, int PreBoarding, int JoiningNext30Days, int CompletedLast30Days, int OverdueTasks, IReadOnlyList<CaseItem> Upcoming, IReadOnlyDictionary<string, int> OverdueByCategory);

/// <summary>
/// Pre-boarding and onboarding (spec §13). A joiner's case starts from the employee-hired event with
/// the best-fitting checklist; every task goes to a person (HR, IT, admin owners, the manager, the buddy
/// or the joiner), is reminded when overdue, and the case completes when its required tasks are done.
/// </summary>
internal sealed class OnboardingService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Templates ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<TemplateItem>> TemplatesAsync(CancellationToken cancellationToken)
    {
        List<OnboardingTemplate> templates = await dbContext.OnboardingTemplates.AsNoTracking().OrderByDescending(row => row.IsDefault).ThenBy(row => row.Name).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, templates.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await organization.GetNamesAsync(OrgUnitKind.Location, templates.Where(row => row.LocationId != null).Select(row => row.LocationId!.Value).Distinct(), cancellationToken);
        return [.. templates.Select(row => new TemplateItem(
            row.Id, row.Name, row.Description, row.DepartmentId, row.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
            row.LocationId, row.LocationId is { } location ? locations.GetValueOrDefault(location) : null, row.EmploymentTypeCode, row.IsDefault, row.IsActive, row.Tasks))];
    }

    public async Task<IReadOnlyList<TemplateItem>> SaveTemplateAsync(Guid? id, TemplateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(body.Details);
        OnboardingTemplate template;
        if (id is { } existing)
        {
            template = await dbContext.OnboardingTemplates.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(OnboardingTemplate), existing);
            if (template.IsDefault && (!body.Details.IsDefault || !body.IsActive))
            {
                throw new DomainRuleException("TEMPLATE_DEFAULT", "Make another checklist the default before changing this one.");
            }

            template.Update(body.Details, body.IsActive);
        }
        else
        {
            template = OnboardingTemplate.Create(body.Details);
            dbContext.OnboardingTemplates.Add(template);
        }

        if (template.IsDefault)
        {
            foreach (OnboardingTemplate other in await dbContext.OnboardingTemplates.Where(row => row.IsDefault && row.Id != template.Id).ToListAsync(cancellationToken))
            {
                other.ClearDefault();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await TemplatesAsync(cancellationToken);
    }

    // ----- Settings -----------------------------------------------------------------------------

    public async Task<OwnersView> OwnersAsync(CancellationToken cancellationToken)
    {
        OnboardingSettings? settings = await dbContext.OnboardingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync([settings?.HrOwnerEmployeeId, settings?.ItOwnerEmployeeId, settings?.AdminOwnerEmployeeId], cancellationToken);
        return new OwnersView(
            settings?.HrOwnerEmployeeId, Name(names, settings?.HrOwnerEmployeeId),
            settings?.ItOwnerEmployeeId, Name(names, settings?.ItOwnerEmployeeId),
            settings?.AdminOwnerEmployeeId, Name(names, settings?.AdminOwnerEmployeeId));
    }

    /// <summary>Saves the task owners; open tasks still waiting for an owner of that role are handed to them.</summary>
    public async Task<OwnersView> SaveOwnersAsync(OwnersBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (Guid owner in new[] { body.HrOwnerEmployeeId, body.ItOwnerEmployeeId, body.AdminOwnerEmployeeId }.OfType<Guid>())
        {
            if (!await employees.IsActiveAsync(owner, cancellationToken))
            {
                throw new DomainRuleException("ONBOARDING_OWNER", "Every task owner must be an active employee.");
            }
        }

        OnboardingSettings? settings = await dbContext.OnboardingSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = OnboardingSettings.Defaults();
            dbContext.OnboardingSettings.Add(settings);
        }

        settings.Update(body.HrOwnerEmployeeId, body.ItOwnerEmployeeId, body.AdminOwnerEmployeeId);
        List<OnboardingTask> unowned = await dbContext.OnboardingTasks
            .Where(row => row.AssigneeEmployeeId == null && row.Status == OnboardingTaskStatus.Pending
                && (row.AssigneeRole == AssigneeRole.Hr || row.AssigneeRole == AssigneeRole.It || row.AssigneeRole == AssigneeRole.Admin))
            .ToListAsync(cancellationToken);
        foreach (OnboardingTask task in unowned)
        {
            task.Assign(task.AssigneeRole switch
            {
                AssigneeRole.Hr => body.HrOwnerEmployeeId,
                AssigneeRole.It => body.ItOwnerEmployeeId,
                _ => body.AdminOwnerEmployeeId,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await OwnersAsync(cancellationToken);
    }

    // ----- Cases --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CaseItem>> CasesAsync(CaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<OnboardingCase> rows = await VisibleCasesAsync(cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.Open == true)
        {
            rows = rows.Where(row => row.Status == CaseStatus.PreBoarding || row.Status == CaseStatus.InProgress);
        }

        if (query.EmployeeId is { } employee)
        {
            rows = rows.Where(row => row.EmployeeId == employee);
        }

        return await CaseItemsAsync(await rows.OrderByDescending(row => row.JoiningDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<CaseView> CaseAsync(Guid id, CancellationToken cancellationToken)
    {
        OnboardingCase onboarding = await (await VisibleCasesAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(OnboardingCase), id);
        return await ViewAsync(onboarding, cancellationToken);
    }

    /// <summary>The joiner's own onboarding, the most recent case.</summary>
    public async Task<CaseView?> MyCaseAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        OnboardingCase? onboarding = await dbContext.OnboardingCases.AsNoTracking()
            .Where(row => row.EmployeeId == me && row.Status != CaseStatus.Cancelled)
            .OrderByDescending(row => row.JoiningDate)
            .FirstOrDefaultAsync(cancellationToken);
        return onboarding is null ? null : await ViewAsync(onboarding, cancellationToken);
    }

    /// <summary>Tasks across every onboarding that fall to the caller.</summary>
    public async Task<IReadOnlyList<TaskItem>> MyTasksAsync(bool includeDone, CancellationToken cancellationToken)
    {
        Guid me = Me();
        List<Guid> open = await dbContext.OnboardingCases.AsNoTracking()
            .Where(row => row.Status == CaseStatus.PreBoarding || row.Status == CaseStatus.InProgress)
            .Select(row => row.Id).ToListAsync(cancellationToken);
        List<OnboardingTask> tasks = await dbContext.OnboardingTasks.AsNoTracking()
            .Where(row => row.AssigneeEmployeeId == me && open.Contains(row.CaseId) && (includeDone || row.Status == OnboardingTaskStatus.Pending))
            .OrderBy(row => row.DueDate).ThenBy(row => row.SortOrder)
            .Take(500)
            .ToListAsync(cancellationToken);
        return await TaskItemsAsync(tasks, canManage: false, cancellationToken);
    }

    public async Task<CaseView> StartAsync(StartBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        EmployeeSummary person = await employees.GetAsync(body.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", body.EmployeeId);
        if (await dbContext.OnboardingCases.AnyAsync(row => row.EmployeeId == body.EmployeeId && (row.Status == CaseStatus.PreBoarding || row.Status == CaseStatus.InProgress), cancellationToken))
        {
            throw new DomainRuleException("ONBOARDING_EXISTS", $"{person.DisplayName} already has an onboarding under way.");
        }

        OnboardingTemplate template = body.TemplateId is { } templateId
            ? await dbContext.OnboardingTemplates.AsNoTracking().FirstOrDefaultAsync(row => row.Id == templateId && row.IsActive, cancellationToken) ?? throw new NotFoundException(nameof(OnboardingTemplate), templateId)
            : await TemplateForAsync(person.DepartmentId, person.LocationId, person.EmploymentTypeCode, cancellationToken)
                ?? throw new DomainRuleException("ONBOARDING_TEMPLATE", "There is no active onboarding checklist; create one first.");
        OnboardingCase onboarding = await CreateCaseAsync(person, person.JoiningDate ?? await clock.TodayAsync(cancellationToken), person.ManagerEmployeeId, body.BuddyEmployeeId, template, cancellationToken);
        return await CaseAsync(onboarding.Id, cancellationToken);
    }

    /// <summary>Starts onboarding for a new hire with the best-fitting checklist. Does nothing when there is none or a case already exists.</summary>
    public async Task StartForHireAsync(Guid employeeId, DateOnly joiningDate, Guid? managerEmployeeId, CancellationToken cancellationToken)
    {
        if (await dbContext.OnboardingCases.AnyAsync(row => row.EmployeeId == employeeId && row.Status != CaseStatus.Cancelled, cancellationToken))
        {
            return;
        }

        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        if (person is null || await TemplateForAsync(person.DepartmentId, person.LocationId, person.EmploymentTypeCode, cancellationToken) is not { } template)
        {
            return;
        }

        await CreateCaseAsync(person, joiningDate, managerEmployeeId ?? person.ManagerEmployeeId, null, template, cancellationToken);
    }

    public async Task<CaseView> AssignBuddyAsync(Guid id, Guid? buddyEmployeeId, CancellationToken cancellationToken)
    {
        OnboardingCase onboarding = await ManagedCaseAsync(id, cancellationToken);
        if (buddyEmployeeId is { } buddy && !await employees.IsActiveAsync(buddy, cancellationToken))
        {
            throw new DomainRuleException("ONBOARDING_BUDDY", "The buddy must be an active employee.");
        }

        onboarding.AssignBuddy(buddyEmployeeId);
        List<OnboardingTask> tasks = await dbContext.OnboardingTasks.Where(row => row.CaseId == id && row.AssigneeRole == AssigneeRole.Buddy && row.Status == OnboardingTaskStatus.Pending).ToListAsync(cancellationToken);
        foreach (OnboardingTask task in tasks)
        {
            task.Assign(buddyEmployeeId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(onboarding, tasks, cancellationToken);
        return await CaseAsync(id, cancellationToken);
    }

    public async Task<CaseView> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        OnboardingCase onboarding = await ManagedCaseAsync(id, cancellationToken);
        onboarding.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await CaseAsync(id, cancellationToken);
    }

    // ----- Tasks --------------------------------------------------------------------------------

    public async Task<TaskItem> FinishTaskAsync(Guid taskId, FinishBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        (OnboardingTask task, OnboardingCase onboarding) = await ActionableTaskAsync(taskId, cancellationToken);
        task.Finish(body.Skipped, currentUser.EmployeeId, body.Notes, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CompleteIfDoneAsync(onboarding, cancellationToken);
        return (await TaskItemsAsync([task], await CanManageAsync(cancellationToken), cancellationToken))[0];
    }

    public async Task<TaskItem> ReopenTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        (OnboardingTask task, OnboardingCase onboarding) = await ActionableTaskAsync(taskId, cancellationToken, allowClosedCase: true);
        if (onboarding.Status == CaseStatus.Cancelled)
        {
            throw new DomainRuleException("ONBOARDING_CLOSED", "The onboarding was cancelled.");
        }

        task.Reopen();
        onboarding.Reopen(await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await TaskItemsAsync([task], await CanManageAsync(cancellationToken), cancellationToken))[0];
    }

    public async Task<CaseView> ChangeTaskAsync(Guid taskId, TaskChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        OnboardingTask task = await dbContext.OnboardingTasks.FirstOrDefaultAsync(row => row.Id == taskId, cancellationToken) ?? throw new NotFoundException(nameof(OnboardingTask), taskId);
        OnboardingCase onboarding = await ManagedCaseAsync(task.CaseId, cancellationToken);
        onboarding.EnsureOpen();
        bool reassigned = body.AssigneeEmployeeId is { } assignee && assignee != task.AssigneeEmployeeId;
        if (reassigned && !await employees.IsActiveAsync(body.AssigneeEmployeeId!.Value, cancellationToken))
        {
            throw new DomainRuleException("ONBOARDING_ASSIGNEE", "The assignee must be an active employee.");
        }

        if (reassigned)
        {
            task.Assign(body.AssigneeEmployeeId);
        }

        if (body.DueDate is { } due)
        {
            task.Reschedule(due);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (reassigned)
        {
            await NotifyAssignedAsync(onboarding, [task], cancellationToken);
        }

        return await CaseAsync(onboarding.Id, cancellationToken);
    }

    // ----- Dashboard and jobs -------------------------------------------------------------------

    public async Task<OnboardingDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<OnboardingCase> cases = await (await VisibleCasesAsync(cancellationToken))
            .Where(row => row.Status == CaseStatus.PreBoarding || row.Status == CaseStatus.InProgress || row.CompletedOn >= today.AddDays(-30))
            .ToListAsync(cancellationToken);
        List<Guid> open = [.. cases.Where(row => row.IsOpen).Select(row => row.Id)];
        List<OnboardingTask> overdue = await dbContext.OnboardingTasks.AsNoTracking()
            .Where(row => open.Contains(row.CaseId) && row.Status == OnboardingTaskStatus.Pending && row.DueDate < today)
            .ToListAsync(cancellationToken);
        return new OnboardingDashboard(
            open.Count,
            cases.Count(row => row.Status == CaseStatus.PreBoarding),
            cases.Count(row => row.IsOpen && row.JoiningDate >= today && row.JoiningDate <= today.AddDays(30)),
            cases.Count(row => row.Status == CaseStatus.Completed),
            overdue.Count,
            await CaseItemsAsync([.. cases.Where(row => row.IsOpen).OrderBy(row => row.JoiningDate < today ? 1 : 0).ThenBy(row => row.JoiningDate).Take(10)], cancellationToken),
            overdue.GroupBy(row => row.Category.ToString()).ToDictionary(group => group.Key, group => group.Count()));
    }

    /// <summary>The daily pass: moves pre-boarding cases on at joining and reminds owners of overdue tasks. Returns the reminders sent.</summary>
    public async Task<int> DailyAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<OnboardingCase> open = await dbContext.OnboardingCases.Where(row => row.Status == CaseStatus.PreBoarding || row.Status == CaseStatus.InProgress).ToListAsync(cancellationToken);
        foreach (OnboardingCase onboarding in open)
        {
            onboarding.Advance(today);
        }

        List<Guid> ids = [.. open.Select(row => row.Id)];
        List<OnboardingTask> tasks = await dbContext.OnboardingTasks
            .Where(row => ids.Contains(row.CaseId) && row.Status == OnboardingTaskStatus.Pending && row.DueDate < today && row.AssigneeEmployeeId != null)
            .ToListAsync(cancellationToken);
        List<OnboardingTask> due = [.. tasks.Where(task => task.NeedsReminder(today))];
        Dictionary<Guid, string> joiners = await NamesAsync(open.Select(row => (Guid?)row.EmployeeId), cancellationToken);
        Dictionary<Guid, OnboardingCase> byId = open.ToDictionary(row => row.Id);
        foreach (OnboardingTask task in due)
        {
            OnboardingCase onboarding = byId[task.CaseId];
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.OnboardingTaskOverdue,
                    [new NotificationRecipient(task.AssigneeEmployeeId)],
                    new Dictionary<string, string>
                    {
                        ["taskTitle"] = task.Title,
                        ["employeeName"] = joiners.GetValueOrDefault(onboarding.EmployeeId) ?? "a new joiner",
                        ["dueDate"] = task.DueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                    },
                    task.AssigneeEmployeeId == onboarding.EmployeeId ? "/me/onboarding" : "/me/onboarding/tasks"),
                cancellationToken);
            task.Reminded(today);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("ONBOARDING_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<OnboardingTemplate?> TemplateForAsync(Guid? departmentId, Guid? locationId, string? employmentTypeCode, CancellationToken cancellationToken)
    {
        List<OnboardingTemplate> templates = await dbContext.OnboardingTemplates.AsNoTracking().Where(row => row.IsActive).ToListAsync(cancellationToken);
        return templates
            .Select(template => (Template: template, Fit: template.Fit(departmentId, locationId, employmentTypeCode)))
            .Where(row => row.Fit >= 0)
            .OrderByDescending(row => row.Fit)
            .ThenByDescending(row => row.Template.IsDefault)
            .Select(row => row.Template)
            .FirstOrDefault();
    }

    private async Task<OnboardingCase> CreateCaseAsync(EmployeeSummary person, DateOnly joiningDate, Guid? managerEmployeeId, Guid? buddyEmployeeId, OnboardingTemplate template, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var onboarding = OnboardingCase.Start(person.Id, template, joiningDate, managerEmployeeId, buddyEmployeeId, today);
        dbContext.OnboardingCases.Add(onboarding);
        OnboardingSettings? owners = await dbContext.OnboardingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        Guid? hr = owners?.HrOwnerEmployeeId ?? person.HrbpEmployeeId;
        var tasks = new List<OnboardingTask>();
        int order = 0;
        foreach (TemplateTask item in template.Tasks)
        {
            Guid? assignee = item.Assignee switch
            {
                AssigneeRole.Hr => hr,
                AssigneeRole.It => owners?.ItOwnerEmployeeId,
                AssigneeRole.Admin => owners?.AdminOwnerEmployeeId,
                AssigneeRole.Manager => managerEmployeeId,
                AssigneeRole.Buddy => buddyEmployeeId,
                _ => person.Id,
            };
            OnboardingTask task = OnboardingTask.From(onboarding.Id, item, joiningDate, assignee, order++);
            tasks.Add(task);
            dbContext.OnboardingTasks.Add(task);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.OnboardingWelcome,
                [new NotificationRecipient(person.Id)],
                new Dictionary<string, string> { ["employeeName"] = person.DisplayName, ["joiningDate"] = joiningDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture) },
                "/me/onboarding"),
            cancellationToken);
        await NotifyAssignedAsync(onboarding, [.. tasks.Where(task => task.AssigneeEmployeeId != person.Id)], cancellationToken);
        return onboarding;
    }

    /// <summary>One message per assignee, naming their first task (the rest are in their list).</summary>
    private async Task NotifyAssignedAsync(OnboardingCase onboarding, List<OnboardingTask> tasks, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        string joiner = (await employees.GetAsync(onboarding.EmployeeId, cancellationToken))?.DisplayName ?? "a new joiner";
        foreach (IGrouping<Guid, OnboardingTask> group in tasks.Where(task => task.AssigneeEmployeeId is not null).GroupBy(task => task.AssigneeEmployeeId!.Value))
        {
            OnboardingTask first = group.OrderBy(task => task.DueDate).First();
            int more = group.Count() - 1;
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.OnboardingTaskAssigned,
                    [new NotificationRecipient(group.Key)],
                    new Dictionary<string, string>
                    {
                        ["taskTitle"] = more > 0 ? $"{first.Title} (and {more} more)" : first.Title,
                        ["employeeName"] = joiner,
                        ["dueDate"] = first.DueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        ["joiningDate"] = onboarding.JoiningDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                    },
                    "/me/onboarding/tasks"),
                cancellationToken);
        }
    }

    private async Task CompleteIfDoneAsync(OnboardingCase onboarding, CancellationToken cancellationToken)
    {
        if (!onboarding.IsOpen)
        {
            return;
        }

        bool pending = await dbContext.OnboardingTasks.AnyAsync(row => row.CaseId == onboarding.Id && row.Required && row.Status == OnboardingTaskStatus.Pending, cancellationToken);
        if (!pending)
        {
            onboarding.Complete(await clock.TodayAsync(cancellationToken));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// A task can be closed by its assignee, by someone who completes onboarding tasks (IT, admin) for a
    /// task of their kind, or by someone who manages the case.
    /// </summary>
    private async Task<(OnboardingTask Task, OnboardingCase Case)> ActionableTaskAsync(Guid taskId, CancellationToken cancellationToken, bool allowClosedCase = false)
    {
        OnboardingTask task = await dbContext.OnboardingTasks.FirstOrDefaultAsync(row => row.Id == taskId, cancellationToken) ?? throw new NotFoundException(nameof(OnboardingTask), taskId);
        OnboardingCase onboarding = await dbContext.OnboardingCases.FirstAsync(row => row.Id == task.CaseId, cancellationToken);
        if (!allowClosedCase)
        {
            onboarding.EnsureOpen();
        }

        if (!await CanActAsync(task, onboarding, cancellationToken))
        {
            throw new NotFoundException(nameof(OnboardingTask), taskId);
        }

        return (task, onboarding);
    }

    private async Task<bool> CanActAsync(OnboardingTask task, OnboardingCase onboarding, CancellationToken cancellationToken)
    {
        if (task.AssigneeEmployeeId is { } assignee && assignee == currentUser.EmployeeId)
        {
            return true;
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        if (granted.Has(Permissions.Onboarding.TaskComplete) && task.AssigneeRole is AssigneeRole.It or AssigneeRole.Admin)
        {
            return true;
        }

        return granted.Has(Permissions.Onboarding.CaseManage) && await employees.IsInScopeAsync(onboarding.EmployeeId, Permissions.Onboarding.CaseManage, cancellationToken);
    }

    private async Task<bool> CanManageAsync(CancellationToken cancellationToken) => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Onboarding.CaseManage);

    private async Task<OnboardingCase> ManagedCaseAsync(Guid id, CancellationToken cancellationToken)
    {
        OnboardingCase onboarding = await dbContext.OnboardingCases.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(OnboardingCase), id);
        return await CanManageAsync(cancellationToken) && await employees.IsInScopeAsync(onboarding.EmployeeId, Permissions.Onboarding.CaseManage, cancellationToken)
            ? onboarding
            : throw new NotFoundException(nameof(OnboardingCase), id);
    }

    private async Task<IQueryable<OnboardingCase>> VisibleCasesAsync(CancellationToken cancellationToken)
    {
        IQueryable<OnboardingCase> rows = dbContext.OnboardingCases.AsNoTracking();
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Onboarding.CaseView))
        {
            return rows.Where(row => false);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Onboarding.CaseView, cancellationToken);
        return scope is null ? rows : rows.Where(row => scope.Contains(row.EmployeeId));
    }

    private async Task<CaseView> ViewAsync(OnboardingCase onboarding, CancellationToken cancellationToken)
    {
        List<OnboardingTask> tasks = await dbContext.OnboardingTasks.AsNoTracking().Where(row => row.CaseId == onboarding.Id).OrderBy(row => row.DueDate).ThenBy(row => row.SortOrder).ToListAsync(cancellationToken);
        bool canManage = await CanManageAsync(cancellationToken) && await employees.IsInScopeAsync(onboarding.EmployeeId, Permissions.Onboarding.CaseManage, cancellationToken);
        return new CaseView((await CaseItemsAsync([onboarding], cancellationToken))[0], await TaskItemsAsync(tasks, canManage, cancellationToken), canManage);
    }

    private async Task<List<CaseItem>> CaseItemsAsync(IReadOnlyList<OnboardingCase> cases, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> ids = [.. cases.Select(row => row.Id)];
        var counts = await dbContext.OnboardingTasks.AsNoTracking().Where(row => ids.Contains(row.CaseId))
            .GroupBy(row => row.CaseId)
            .Select(group => new
            {
                CaseId = group.Key,
                Total = group.Count(),
                Done = group.Count(row => row.Status != OnboardingTaskStatus.Pending),
                Overdue = group.Count(row => row.Status == OnboardingTaskStatus.Pending && row.DueDate < today),
            })
            .ToDictionaryAsync(row => row.CaseId, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(
                cases.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.ManagerEmployeeId, row.BuddyEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(
            OrgUnitKind.Department, people.Values.Where(person => person.DepartmentId != null).Select(person => person.DepartmentId!.Value).Distinct(), cancellationToken);
        return [.. cases.Select(row =>
        {
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            int total = counts.TryGetValue(row.Id, out var count) ? count.Total : 0;
            int done = count?.Done ?? 0;
            return new CaseItem(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, person?.Designation,
                person?.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null, row.TemplateName, row.JoiningDate,
                row.ManagerEmployeeId, row.ManagerEmployeeId is { } manager ? people.GetValueOrDefault(manager)?.DisplayName : null,
                row.BuddyEmployeeId, row.BuddyEmployeeId is { } buddy ? people.GetValueOrDefault(buddy)?.DisplayName : null,
                row.Status, row.CompletedOn, total, done, row.IsOpen ? count?.Overdue ?? 0 : 0, total == 0 ? 0 : (int)Math.Round(100m * done / total));
        })];
    }

    private async Task<List<TaskItem>> TaskItemsAsync(IReadOnlyList<OnboardingTask> tasks, bool canManage, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(tasks.Select(row => row.AssigneeEmployeeId).Concat(tasks.Select(row => row.CompletedByEmployeeId)), cancellationToken);
        bool completes = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Onboarding.TaskComplete);
        Guid? me = currentUser.EmployeeId;
        return [.. tasks.Select(row => new TaskItem(
            row.Id, row.CaseId, row.Title, row.Description, row.Category, row.AssigneeRole, row.AssigneeEmployeeId, Name(names, row.AssigneeEmployeeId), row.DueDate,
            row.Required, row.Status, row.CompletedDate, Name(names, row.CompletedByEmployeeId), row.Notes, row.IsOverdue(today),
            canManage || (me is not null && row.AssigneeEmployeeId == me) || (completes && row.AssigneeRole is AssigneeRole.It or AssigneeRole.Admin)))];
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
}

/// <summary>Seeds a tenant's default onboarding checklist.</summary>
public sealed class OnboardingSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.OnboardingTemplates.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            return 0;
        }

        dbContext.OnboardingTemplates.Add(OnboardingTemplate.Create(new TemplateDetails(
            OnboardingDefaults.DefaultTemplateName, "Pre-boarding through the first month, for every joiner.", null, null, null, IsDefault: true, OnboardingDefaults.Tasks)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }
}
