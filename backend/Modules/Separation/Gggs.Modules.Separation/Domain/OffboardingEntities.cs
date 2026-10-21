using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Separation.Domain;

/// <summary>Who does an offboarding task. Manager, HR and successor resolve to a person; IT, admin and finance are team queues.</summary>
public enum OffboardingOwner
{
    Manager = 1,
    It = 2,
    Admin = 3,
    Finance = 4,
    Hr = 5,
    Employee = 6,
    Successor = 7,
}

/// <summary>One step of an offboarding checklist, due a number of days from the last working day (negative is before).</summary>
public sealed record OffboardingStep(string Title, string? Description, OffboardingOwner Owner, int DueOffsetDays, bool Required = true);

public sealed record OffboardingTemplateDetails(string Name, string? Description, Guid? DepartmentId, Guid? LocationId, string? EmploymentTypeCode, bool IsDefault, IReadOnlyList<OffboardingStep> Steps);

/// <summary>
/// A configurable offboarding checklist: the actions around a leaver (handover, access, assets,
/// knowledge transfer), alongside clearance which records "no dues". The most specific active
/// template for the leaver's department, location and employment type is used; otherwise the default.
/// </summary>
public sealed class OffboardingTemplate : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OffboardingTemplate()
    {
        Name = null!;
        StepsJson = "[]";
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? LocationId { get; private set; }

    public string? EmploymentTypeCode { get; private set; }

    public bool IsDefault { get; private set; }

    public string StepsJson { get; private set; }

    public IReadOnlyList<OffboardingStep> Steps => JsonSerializer.Deserialize<List<OffboardingStep>>(StepsJson, Json) ?? [];

    public static OffboardingTemplate Create(OffboardingTemplateDetails details)
    {
        var template = new OffboardingTemplate();
        template.Update(details, active: true);
        return template;
    }

    /// <summary>The checklist a tenant starts with: the usual Indian offboarding steps.</summary>
    public static OffboardingTemplate Standard() => Create(new OffboardingTemplateDetails(
        "Standard offboarding",
        "Handover, access, assets and exit formalities for every leaver.",
        null,
        null,
        null,
        IsDefault: true,
        [
            new("Agree a handover plan with the successor", "List open work, owners and documents to hand over.", OffboardingOwner.Manager, -21),
            new("Hand over work and documents", "Walk the successor through open work, contacts and where things live.", OffboardingOwner.Employee, -5),
            new("Take over the leaver's work", "Confirm you have what you need to carry the work on.", OffboardingOwner.Successor, -3),
            new("Back up mailbox and files", "Archive the mailbox and move shared files to the team drive.", OffboardingOwner.It, -2),
            new("Collect laptop, ID card and access card", null, OffboardingOwner.Admin, 0),
            new("Disable accounts and VPN", "PeopleOS sign-in is switched off automatically after the last day.", OffboardingOwner.It, 0),
            new("Close corporate card and recover advances", null, OffboardingOwner.Finance, 0, Required: false),
            new("Exit interview and documents", "Relieving letter, experience letter and Form 16 handover.", OffboardingOwner.Hr, 0),
        ]));

    public void Update(OffboardingTemplateDetails details, bool active)
    {
        ArgumentNullException.ThrowIfNull(details);
        List<OffboardingStep> steps = [.. (details.Steps ?? []).Select(step => step with
        {
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(step.Title).Trim(), 200),
            Description = string.IsNullOrWhiteSpace(step.Description) ? null : Guard.MaxLength(step.Description.Trim(), 1000),
        })];
        if (steps.Count is 0 or > 100)
        {
            throw new DomainRuleException("OFFBOARDING_STEPS", "A checklist has between 1 and 100 steps.");
        }

        if (steps.Any(step => step.DueOffsetDays is < -90 or > 30 || !Enum.IsDefined(step.Owner)))
        {
            throw new DomainRuleException("OFFBOARDING_STEPS", "Each step needs an owner and a due day between 90 days before and 30 days after the last working day.");
        }

        if (details.IsDefault && (details.DepartmentId is not null || details.LocationId is not null || !string.IsNullOrWhiteSpace(details.EmploymentTypeCode)))
        {
            throw new DomainRuleException("OFFBOARDING_DEFAULT", "The default checklist applies to everyone; leave department, location and employment type empty.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 1000);
        DepartmentId = details.DepartmentId;
        LocationId = details.LocationId;
        EmploymentTypeCode = string.IsNullOrWhiteSpace(details.EmploymentTypeCode) ? null : Guard.MaxLength(details.EmploymentTypeCode.Trim().ToUpperInvariant(), 64);
        IsDefault = details.IsDefault;
        StepsJson = JsonSerializer.Serialize(steps.OrderBy(step => step.DueOffsetDays).ToList(), Json);
        IsActive = active;
    }

    public void ClearDefault() => IsDefault = false;

    /// <summary>-1 when the template does not apply to the leaver; otherwise one point per matching criterion.</summary>
    public int Fit(Guid? departmentId, Guid? locationId, string? employmentTypeCode)
    {
        if (!IsActive
            || (DepartmentId is { } department && department != departmentId)
            || (LocationId is { } location && location != locationId)
            || (EmploymentTypeCode is { } type && !string.Equals(type, employmentTypeCode, StringComparison.OrdinalIgnoreCase)))
        {
            return -1;
        }

        return (DepartmentId is null ? 0 : 1) + (LocationId is null ? 0 : 1) + (EmploymentTypeCode is null ? 0 : 1);
    }
}

public enum OffboardingTaskStatus
{
    Pending = 1,
    Done = 2,
    Skipped = 3,
}

/// <summary>One step of a leaver's offboarding, owned by a person or a team, due relative to the last day.</summary>
public sealed class OffboardingTask : TenantAggregateRoot
{
    private OffboardingTask()
    {
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public OffboardingOwner Owner { get; private set; }

    public Guid? AssigneeEmployeeId { get; private set; }

    public int DueOffsetDays { get; private set; }

    public DateOnly DueDate { get; private set; }

    public bool Required { get; private set; }

    public int SortOrder { get; private set; }

    public OffboardingTaskStatus Status { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public Guid? CompletedByEmployeeId { get; private set; }

    public string? Notes { get; private set; }

    public DateOnly? LastRemindedOn { get; private set; }

    public bool IsOpen => Status == OffboardingTaskStatus.Pending;

    public static OffboardingTask From(Guid caseId, OffboardingStep step, DateOnly lastWorkingDay, Guid? assigneeEmployeeId, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new OffboardingTask
        {
            CaseId = Guard.NotEmpty(caseId),
            Title = step.Title,
            Description = step.Description,
            Owner = step.Owner,
            AssigneeEmployeeId = assigneeEmployeeId,
            DueOffsetDays = step.DueOffsetDays,
            DueDate = lastWorkingDay.AddDays(step.DueOffsetDays),
            Required = step.Required,
            SortOrder = sortOrder,
            Status = OffboardingTaskStatus.Pending,
        };
    }

    public void Assign(Guid? assigneeEmployeeId) => AssigneeEmployeeId = assigneeEmployeeId;

    /// <summary>Keeps the task on its offset when the last working day moves.</summary>
    public void Follow(DateOnly lastWorkingDay)
    {
        if (IsOpen)
        {
            DueDate = lastWorkingDay.AddDays(DueOffsetDays);
            LastRemindedOn = null;
        }
    }

    public void Finish(bool skipped, Guid? byEmployeeId, string? notes, DateTime now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("OFFBOARDING_TASK_STATE", "The task is already closed.");
        }

        if (skipped && Required)
        {
            throw new DomainRuleException("OFFBOARDING_TASK_REQUIRED", "A required task cannot be skipped.");
        }

        Status = skipped ? OffboardingTaskStatus.Skipped : OffboardingTaskStatus.Done;
        CompletedAt = now;
        CompletedByEmployeeId = byEmployeeId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }

    public void Reopen()
    {
        Status = OffboardingTaskStatus.Pending;
        CompletedAt = null;
        CompletedByEmployeeId = null;
    }

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate < today;

    public bool NeedsReminder(DateOnly today) => IsOverdue(today) && AssigneeEmployeeId is not null && (LastRemindedOn is null || LastRemindedOn.Value.AddDays(3) <= today);

    public void Reminded(DateOnly today) => LastRemindedOn = today;
}

/// <summary>What a leaver hands over: people who report to them, project seats and tasks, approvals waiting on them.</summary>
public enum HandoverKind
{
    DirectReport = 1,
    ProjectAllocation = 2,
    ProjectTasks = 3,
    Approvals = 4,
}

public enum HandoverStatus
{
    Open = 1,
    Done = 2,
    Skipped = 3,
}

/// <summary>
/// One thing to hand over, found by looking across modules when the separation is approved (and
/// again on refresh). HR carries it out in one click; the change is made in the owning module.
/// </summary>
public sealed class HandoverItem : TenantAggregateRoot
{
    private HandoverItem()
    {
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    public HandoverKind Kind { get; private set; }

    /// <summary>The employee, project seat or task the item is about; null for the aggregate items.</summary>
    public Guid? SourceId { get; private set; }

    public string Title { get; private set; }

    public string? Detail { get; private set; }

    public Guid? ToEmployeeId { get; private set; }

    public HandoverStatus Status { get; private set; }

    public DateTime? DoneAt { get; private set; }

    public Guid? DoneByEmployeeId { get; private set; }

    public string? Outcome { get; private set; }

    public bool IsOpen => Status == HandoverStatus.Open;

    public static HandoverItem Raise(Guid caseId, HandoverKind kind, Guid? sourceId, string title, string? detail, Guid? toEmployeeId) => new()
    {
        CaseId = Guard.NotEmpty(caseId),
        Kind = kind,
        SourceId = sourceId,
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 300),
        Detail = string.IsNullOrWhiteSpace(detail) ? null : Guard.MaxLength(detail.Trim(), 2000),
        ToEmployeeId = toEmployeeId,
        Status = HandoverStatus.Open,
    };

    /// <summary>Brings an open aggregate item up to date (the count of tasks or approvals changes).</summary>
    public void Refresh(string title, string? detail)
    {
        if (IsOpen)
        {
            Title = Guard.MaxLength(title.Trim(), 300);
            Detail = string.IsNullOrWhiteSpace(detail) ? null : Guard.MaxLength(detail.Trim(), 2000);
        }
    }

    public void Target(Guid? toEmployeeId)
    {
        if (IsOpen)
        {
            ToEmployeeId = toEmployeeId;
        }
    }

    /// <summary>Whether carrying the item out needs someone to receive it (releasing a project seat does not).</summary>
    public bool NeedsRecipient => Kind != HandoverKind.ProjectAllocation;

    public void Close(bool skipped, Guid? byEmployeeId, string? outcome, DateTime now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("HANDOVER_STATE", "This handover item is already closed.");
        }

        Status = skipped ? HandoverStatus.Skipped : HandoverStatus.Done;
        DoneAt = now;
        DoneByEmployeeId = byEmployeeId;
        Outcome = string.IsNullOrWhiteSpace(outcome) ? null : Guard.MaxLength(outcome.Trim(), 500);
    }
}
