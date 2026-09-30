using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Onboarding;

/// <summary>Who a template task falls to - a fixed set of roles rather than a real permission or
/// user reference, matching how little v1 onboarding needs to route work (architecture.md's
/// <c>onb.OnboardingTemplateTask.AssigneeRole</c>).</summary>
public enum OnboardingAssigneeRole
{
    NewHire = 1,
    Manager = 2,
    Hr = 3,
    It = 4,
    Admin = 5,
}

/// <summary>
/// A reusable checklist for bringing someone on board - "Standard Engineering Onboarding," say.
/// v1 has no <c>AppliesToJson</c> targeting (architecture.md §7.2): a tenant picks a template by
/// hand when starting a case rather than the system auto-selecting one by legal entity/branch/
/// designation, the same simplification Leave v1 made for policy assignment before anything needed
/// the targeting rules to actually differ per template.
/// </summary>
public sealed class OnboardingTemplate : TenantAggregateRoot
{
    private OnboardingTemplate()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public static OnboardingTemplate Create(string name) => new()
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
        IsActive = true,
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150);
}

/// <summary>One checklist item on a template - "Issue laptop," "Complete tax declaration." Copied
/// into a real <see cref="OnboardingTask"/> per case at <see cref="OnboardingCase.Create"/> time,
/// the same "template row -> case row" split <c>LeavePolicyRule</c> and a posted
/// <c>LeaveLedgerEntry</c> have, so editing a template later never rewrites history for cases
/// already started from it.</summary>
public sealed class OnboardingTemplateTask : TenantAggregateRoot
{
    private OnboardingTemplateTask()
    {
        Title = null!;
    }

    public Guid TemplateId { get; private set; }

    public string Title { get; private set; }

    public OnboardingAssigneeRole AssigneeRole { get; private set; }

    /// <summary>Days after the case's <see cref="OnboardingCase.StartDate"/> this task falls due -
    /// negative for pre-boarding tasks (e.g. -3 for "send welcome email").</summary>
    public int DueOffsetDays { get; private set; }

    public bool IsMandatory { get; private set; }

    public static OnboardingTemplateTask Create(
        Guid templateId, string title, OnboardingAssigneeRole assigneeRole, int dueOffsetDays, bool isMandatory) => new()
        {
            TemplateId = Guard.NotEmpty(templateId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            AssigneeRole = assigneeRole,
            DueOffsetDays = dueOffsetDays,
            IsMandatory = isMandatory,
            IsActive = true,
        };
}
