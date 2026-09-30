using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Onboarding;

public enum OnboardingCaseStatus
{
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// One employee's run through an <see cref="OnboardingTemplate"/>'s checklist. Started manually
/// against an employee id in this v1 slice - no automatic trigger from an accepted
/// <c>Recruitment.Offer</c> yet (that handoff, publishing from Talent's own recruitment side and
/// consuming here or creating the Core HR employee first, is deferred rather than bundled into this
/// slice). <see cref="EmployeeId"/> is stored as-is, the same "no synchronous cross-service
/// validation" choice <see cref="Recruitment.JobRequisition"/>'s org references made - a wrong
/// employee id on a case is a low-stakes mistake an HR admin catches, not a data-integrity risk
/// worth a call back to Core HR on every case start.
/// </summary>
public sealed class OnboardingCase : TenantAggregateRoot
{
    private OnboardingCase()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid TemplateId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public OnboardingCaseStatus Status { get; private set; }

    /// <summary>Recomputed by <see cref="OnboardingService"/> whenever a task completes - the count
    /// of completed tasks over the total, not a value the domain tracks incrementally itself, so it
    /// can never drift from what the task rows actually say.</summary>
    public int CompletionPercent { get; private set; }

    public static OnboardingCase Create(Guid employeeId, Guid templateId, DateOnly startDate) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        TemplateId = Guard.NotEmpty(templateId),
        StartDate = startDate,
        Status = OnboardingCaseStatus.InProgress,
        CompletionPercent = 0,
        IsActive = true,
    };

    public void UpdateCompletion(int completedCount, int totalCount)
    {
        Guard.Against(totalCount <= 0, "ONBOARDING_CASE_HAS_NO_TASKS", "A case with no tasks cannot compute completion.");

        CompletionPercent = completedCount * 100 / totalCount;

        if (CompletionPercent >= 100 && Status == OnboardingCaseStatus.InProgress)
        {
            Status = OnboardingCaseStatus.Completed;
        }
    }

    public void Cancel()
    {
        Guard.Against(
            Status != OnboardingCaseStatus.InProgress,
            "ONBOARDING_CASE_NOT_IN_PROGRESS",
            $"This case is already {Status} and cannot be cancelled.");

        Status = OnboardingCaseStatus.Cancelled;
    }
}

public enum OnboardingTaskStatus
{
    NotStarted = 1,
    Completed = 2,
}

/// <summary>One task instance on a case, copied from an <see cref="OnboardingTemplateTask"/> at
/// case-creation time - see that type's own remarks on why this is a copy, not a live reference.</summary>
public sealed class OnboardingTask : TenantAggregateRoot
{
    private OnboardingTask()
    {
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    public string Title { get; private set; }

    public OnboardingAssigneeRole AssigneeRole { get; private set; }

    public DateOnly DueDate { get; private set; }

    public bool IsMandatory { get; private set; }

    public OnboardingTaskStatus Status { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public static OnboardingTask Create(
        Guid caseId, string title, OnboardingAssigneeRole assigneeRole, DateOnly dueDate, bool isMandatory) => new()
        {
            CaseId = Guard.NotEmpty(caseId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            AssigneeRole = assigneeRole,
            DueDate = dueDate,
            IsMandatory = isMandatory,
            Status = OnboardingTaskStatus.NotStarted,
            IsActive = true,
        };

    public void Complete(DateTime completedDate)
    {
        Guard.Against(
            Status != OnboardingTaskStatus.NotStarted,
            "ONBOARDING_TASK_ALREADY_COMPLETED",
            "This task is already completed.");

        Status = OnboardingTaskStatus.Completed;
        CompletedDate = completedDate;
    }
}
