using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Onboarding.Domain;

public enum TaskCategory
{
    Documents = 1,
    Verification = 2,
    EmployeeSetup = 3,
    Email = 4,
    Laptop = 5,
    Software = 6,
    Vpn = 7,
    Access = 8,
    IdCard = 9,
    Induction = 10,
    Training = 11,
    ManagerIntroduction = 12,
    Other = 13,
}

/// <summary>Who a task falls to. Resolved to a person when the case starts.</summary>
public enum AssigneeRole
{
    Hr = 1,
    It = 2,
    Admin = 3,
    Manager = 4,
    Buddy = 5,
    Employee = 6,
}

/// <summary>
/// A task in a template. <see cref="DueOffsetDays"/> counts from the joining date: negative is
/// pre-boarding (before day one), zero is the joining day.
/// </summary>
public sealed record TemplateTask(string Title, string? Description, TaskCategory Category, AssigneeRole Assignee, int DueOffsetDays, bool Required = true);

public sealed record TemplateDetails(string Name, string? Description, Guid? DepartmentId, Guid? LocationId, string? EmploymentTypeCode, bool IsDefault, IReadOnlyList<TemplateTask> Tasks);

/// <summary>
/// A configurable onboarding checklist (spec §13). The most specific active template for a joiner's
/// department, location and employment type is used; otherwise the default.
/// </summary>
public sealed class OnboardingTemplate : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OnboardingTemplate()
    {
        Name = null!;
        TasksJson = "[]";
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? LocationId { get; private set; }

    public string? EmploymentTypeCode { get; private set; }

    public bool IsDefault { get; private set; }

    public string TasksJson { get; private set; }

    public IReadOnlyList<TemplateTask> Tasks => JsonSerializer.Deserialize<List<TemplateTask>>(TasksJson, Json) ?? [];

    public static OnboardingTemplate Create(TemplateDetails details)
    {
        var template = new OnboardingTemplate();
        template.Update(details, active: true);
        return template;
    }

    public void Update(TemplateDetails details, bool active)
    {
        ArgumentNullException.ThrowIfNull(details);
        List<TemplateTask> tasks = [.. (details.Tasks ?? []).Select(task => task with
        {
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(task.Title).Trim(), 200),
            Description = string.IsNullOrWhiteSpace(task.Description) ? null : Guard.MaxLength(task.Description.Trim(), 1000),
        })];
        if (tasks.Count is 0 or > 100)
        {
            throw new DomainRuleException("TEMPLATE_TASKS", "A checklist has between 1 and 100 tasks.");
        }

        if (tasks.Any(task => task.DueOffsetDays is < -90 or > 180 || !Enum.IsDefined(task.Category) || !Enum.IsDefined(task.Assignee)))
        {
            throw new DomainRuleException("TEMPLATE_TASKS", "Each task needs a category, an assignee and a due day between 90 days before and 180 days after joining.");
        }

        if (details.IsDefault && (details.DepartmentId is not null || details.LocationId is not null || !string.IsNullOrWhiteSpace(details.EmploymentTypeCode)))
        {
            throw new DomainRuleException("TEMPLATE_DEFAULT", "The default checklist applies to everyone; leave department, location and employment type empty.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 1000);
        DepartmentId = details.DepartmentId;
        LocationId = details.LocationId;
        EmploymentTypeCode = string.IsNullOrWhiteSpace(details.EmploymentTypeCode) ? null : Guard.MaxLength(details.EmploymentTypeCode.Trim().ToUpperInvariant(), 64);
        IsDefault = details.IsDefault;
        TasksJson = JsonSerializer.Serialize(tasks.OrderBy(task => task.DueOffsetDays).ToList(), Json);
        IsActive = active;
    }

    public void ClearDefault() => IsDefault = false;

    /// <summary>
    /// How well the template fits a joiner: -1 when it does not apply, otherwise one point per matching
    /// criterion (the default scores 0, so any specific template that applies beats it).
    /// </summary>
    public int Fit(Guid? departmentId, Guid? locationId, string? employmentTypeCode)
    {
        if (!IsActive)
        {
            return -1;
        }

        if ((DepartmentId is { } department && department != departmentId)
            || (LocationId is { } location && location != locationId)
            || (EmploymentTypeCode is { } type && !string.Equals(type, employmentTypeCode, StringComparison.OrdinalIgnoreCase)))
        {
            return -1;
        }

        if (!IsDefault && DepartmentId is null && LocationId is null && EmploymentTypeCode is null)
        {
            return 0;
        }

        return (DepartmentId is null ? 0 : 1) + (LocationId is null ? 0 : 1) + (EmploymentTypeCode is null ? 0 : 1);
    }
}

public enum CaseStatus
{
    PreBoarding = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
}

/// <summary>A joiner's onboarding (spec §13): the checklist made concrete, with people and due dates.</summary>
public sealed class OnboardingCase : TenantAggregateRoot
{
    private OnboardingCase()
    {
        TemplateName = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid? TemplateId { get; private set; }

    public string TemplateName { get; private set; }

    public DateOnly JoiningDate { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public Guid? BuddyEmployeeId { get; private set; }

    public CaseStatus Status { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public bool IsOpen => Status is CaseStatus.PreBoarding or CaseStatus.InProgress;

    public static OnboardingCase Start(Guid employeeId, OnboardingTemplate template, DateOnly joiningDate, Guid? managerEmployeeId, Guid? buddyEmployeeId, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new OnboardingCase
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            TemplateId = template.Id,
            TemplateName = template.Name,
            JoiningDate = joiningDate,
            ManagerEmployeeId = managerEmployeeId,
            BuddyEmployeeId = buddyEmployeeId,
            Status = today < joiningDate ? CaseStatus.PreBoarding : CaseStatus.InProgress,
        };
    }

    public void AssignBuddy(Guid? buddyEmployeeId)
    {
        EnsureOpen();
        if (buddyEmployeeId == EmployeeId)
        {
            throw new DomainRuleException("ONBOARDING_BUDDY", "A joiner cannot be their own buddy.");
        }

        BuddyEmployeeId = buddyEmployeeId;
    }

    /// <summary>Moves a pre-boarding case on once the joining day arrives.</summary>
    public bool Advance(DateOnly today)
    {
        if (Status == CaseStatus.PreBoarding && today >= JoiningDate)
        {
            Status = CaseStatus.InProgress;
            return true;
        }

        return false;
    }

    public void Complete(DateOnly today)
    {
        EnsureOpen();
        Status = CaseStatus.Completed;
        CompletedOn = today;
    }

    public void Reopen(DateOnly today)
    {
        if (Status == CaseStatus.Completed)
        {
            Status = today < JoiningDate ? CaseStatus.PreBoarding : CaseStatus.InProgress;
            CompletedOn = null;
        }
    }

    public void Cancel()
    {
        EnsureOpen();
        Status = CaseStatus.Cancelled;
    }

    public void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("ONBOARDING_CLOSED", $"The onboarding is {Status.ToString().ToLowerInvariant()}.");
        }
    }
}

public enum OnboardingTaskStatus
{
    Pending = 1,
    Done = 2,
    Skipped = 3,
}

/// <summary>One task of a joiner's onboarding, owned by a person.</summary>
public sealed class OnboardingTask : TenantAggregateRoot
{
    private OnboardingTask()
    {
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public TaskCategory Category { get; private set; }

    public AssigneeRole AssigneeRole { get; private set; }

    public Guid? AssigneeEmployeeId { get; private set; }

    public DateOnly DueDate { get; private set; }

    public bool Required { get; private set; }

    public int SortOrder { get; private set; }

    public OnboardingTaskStatus Status { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public Guid? CompletedByEmployeeId { get; private set; }

    public string? Notes { get; private set; }

    public DateOnly? LastRemindedOn { get; private set; }

    public bool IsOpen => Status == OnboardingTaskStatus.Pending;

    public static OnboardingTask From(Guid caseId, TemplateTask task, DateOnly joiningDate, Guid? assigneeEmployeeId, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new OnboardingTask
        {
            CaseId = Guard.NotEmpty(caseId),
            Title = task.Title,
            Description = task.Description,
            Category = task.Category,
            AssigneeRole = task.Assignee,
            AssigneeEmployeeId = assigneeEmployeeId,
            DueDate = joiningDate.AddDays(task.DueOffsetDays),
            Required = task.Required,
            SortOrder = sortOrder,
            Status = OnboardingTaskStatus.Pending,
        };
    }

    public void Assign(Guid? assigneeEmployeeId) => AssigneeEmployeeId = assigneeEmployeeId;

    public void Reschedule(DateOnly dueDate)
    {
        DueDate = dueDate;
        LastRemindedOn = null;
    }

    public void Finish(bool skipped, Guid? byEmployeeId, string? notes, DateTime now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("ONBOARDING_TASK_STATE", "The task is already closed.");
        }

        if (skipped && Required)
        {
            throw new DomainRuleException("ONBOARDING_TASK_REQUIRED", "A required task cannot be skipped.");
        }

        Status = skipped ? OnboardingTaskStatus.Skipped : OnboardingTaskStatus.Done;
        CompletedDate = now;
        CompletedByEmployeeId = byEmployeeId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }

    public void Reopen()
    {
        Status = OnboardingTaskStatus.Pending;
        CompletedDate = null;
        CompletedByEmployeeId = null;
    }

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate < today;

    /// <summary>Whether an overdue reminder is due: once on becoming overdue, then every three days.</summary>
    public bool NeedsReminder(DateOnly today) => IsOverdue(today) && AssigneeEmployeeId is not null && (LastRemindedOn is null || LastRemindedOn.Value.AddDays(3) <= today);

    public void Reminded(DateOnly today) => LastRemindedOn = today;
}

/// <summary>Who owns the HR, IT and admin tasks of every onboarding. One row per tenant.</summary>
public sealed class OnboardingSettings : TenantAggregateRoot
{
    private OnboardingSettings()
    {
    }

    public Guid? HrOwnerEmployeeId { get; private set; }

    public Guid? ItOwnerEmployeeId { get; private set; }

    public Guid? AdminOwnerEmployeeId { get; private set; }

    public static OnboardingSettings Defaults() => new();

    public void Update(Guid? hr, Guid? it, Guid? admin)
    {
        HrOwnerEmployeeId = hr;
        ItOwnerEmployeeId = it;
        AdminOwnerEmployeeId = admin;
    }
}

/// <summary>The checklist every tenant starts with (spec §13).</summary>
public static class OnboardingDefaults
{
    public const string DefaultTemplateName = "Standard onboarding";

    public static IReadOnlyList<TemplateTask> Tasks { get; } =
    [
        new("Submit joining documents", "ID and address proof, education and previous employment documents, photograph.", TaskCategory.Documents, AssigneeRole.Employee, -7),
        new("Complete background verification", "Initiate and track identity, education and employment checks.", TaskCategory.Verification, AssigneeRole.Hr, -5),
        new("Complete employee record and payroll details", "Statutory IDs, bank account and emergency contacts.", TaskCategory.EmployeeSetup, AssigneeRole.Hr, -3),
        new("Create official email account", null, TaskCategory.Email, AssigneeRole.It, -2),
        new("Prepare and allocate laptop", null, TaskCategory.Laptop, AssigneeRole.It, -1),
        new("Install standard software", null, TaskCategory.Software, AssigneeRole.It, -1),
        new("Set up VPN access", null, TaskCategory.Vpn, AssigneeRole.It, 0),
        new("Grant application and building access", null, TaskCategory.Access, AssigneeRole.Admin, 0),
        new("Issue ID card", null, TaskCategory.IdCard, AssigneeRole.Admin, 0),
        new("Induction session", "Company overview, policies, benefits and tools.", TaskCategory.Induction, AssigneeRole.Hr, 0),
        new("Introduce to the team and set first-month goals", null, TaskCategory.ManagerIntroduction, AssigneeRole.Manager, 0),
        new("Buddy check-in", "Show the joiner around and answer day-to-day questions.", TaskCategory.ManagerIntroduction, AssigneeRole.Buddy, 1, Required: false),
        new("Complete mandatory training", "Code of conduct, information security and POSH.", TaskCategory.Training, AssigneeRole.Employee, 14),
        new("30-day check-in", null, TaskCategory.ManagerIntroduction, AssigneeRole.Manager, 30, Required: false),
    ];
}
