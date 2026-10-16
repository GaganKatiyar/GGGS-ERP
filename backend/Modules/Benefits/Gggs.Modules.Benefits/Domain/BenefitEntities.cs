using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Benefits.Domain;

/// <summary>The kinds of benefit the spec names (§24).</summary>
public enum BenefitType
{
    Health = 1,
    Life = 2,
    Accident = 3,
    Meal = 4,
    Transport = 5,
    Phone = 6,
    Internet = 7,
    Wellness = 8,
    Retirement = 9,
}

/// <summary>
/// A benefit plan (spec §24): who provides it, what it covers, what the employee and the employer pay
/// each month, whether dependents can be added, who is eligible, and how people join.
/// </summary>
public sealed class BenefitPlan : TenantAggregateRoot
{
    private BenefitPlan()
    {
        Code = null!;
        Name = null!;
        Currency = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public BenefitType Type { get; private set; }

    public string? Provider { get; private set; }

    public string? Description { get; private set; }

    public decimal? CoverageAmount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The employee's monthly share for themselves - deducted in payroll.</summary>
    public decimal EmployeeContribution { get; private set; }

    /// <summary>The employer's monthly share - shown on the payslip and in total rewards.</summary>
    public decimal EmployerContribution { get; private set; }

    /// <summary>What each dependent adds to the employee's monthly share.</summary>
    public decimal DependentContribution { get; private set; }

    public bool AllowsDependents { get; private set; }

    public int MaxDependents { get; private set; }

    /// <summary>Employment type codes that may join; empty means everyone.</summary>
    public string EligibleEmploymentTypes { get; private set; } = string.Empty;

    /// <summary>Months of service before someone may join.</summary>
    public int MinServiceMonths { get; private set; }

    /// <summary>New joiners are enrolled automatically.</summary>
    public bool AutoEnroll { get; private set; }

    /// <summary>Cannot be opted out of (group health insurance, usually).</summary>
    public bool Mandatory { get; private set; }

    /// <summary>Self-enrollment goes through the approval workflow.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Employees can enroll themselves now.</summary>
    public bool EnrollmentOpen { get; private set; }

    /// <summary>The employee's share qualifies for a tax deduction (health insurance under section 80D).</summary>
    public bool EmployeeShareTaxDeductible { get; private set; }

    public static BenefitPlan Create(string code, PlanDetails details)
    {
        var plan = new BenefitPlan { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30) };
        plan.Update(details);
        return plan;
    }

    public void Update(PlanDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.EmployeeContribution < 0 || details.EmployerContribution < 0 || details.DependentContribution < 0 || details.CoverageAmount < 0)
        {
            throw new DomainRuleException("PLAN_AMOUNTS", "Contributions and coverage cannot be negative.");
        }

        if (details.MaxDependents is < 0 or > 20 || details.MinServiceMonths is < 0 or > 120)
        {
            throw new DomainRuleException("PLAN_RULES", "Up to 20 dependents, and up to 120 months of service before joining.");
        }

        if (details.Mandatory && !details.AutoEnroll)
        {
            throw new DomainRuleException("PLAN_RULES", "A mandatory plan enrolls people automatically.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Type = details.Type;
        Provider = Text(details.Provider, 200);
        Description = Text(details.Description, 2000);
        CoverageAmount = details.CoverageAmount;
        Currency = Text(details.Currency, 3)?.ToUpperInvariant() ?? "INR";
        EmployeeContribution = Math.Round(details.EmployeeContribution, 2);
        EmployerContribution = Math.Round(details.EmployerContribution, 2);
        DependentContribution = Math.Round(details.DependentContribution, 2);
        AllowsDependents = details.AllowsDependents;
        MaxDependents = details.AllowsDependents ? details.MaxDependents : 0;
        EligibleEmploymentTypes = string.Join(',', (details.EligibleEmploymentTypes ?? []).Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToUpperInvariant()).Distinct());
        MinServiceMonths = details.MinServiceMonths;
        AutoEnroll = details.AutoEnroll;
        Mandatory = details.Mandatory;
        RequiresApproval = details.RequiresApproval;
        EnrollmentOpen = details.EnrollmentOpen;
        EmployeeShareTaxDeductible = details.EmployeeShareTaxDeductible;
    }

    public IReadOnlyList<string> EmploymentTypes => EligibleEmploymentTypes.Length == 0 ? [] : EligibleEmploymentTypes.Split(',');

    /// <summary>Why someone cannot join, or null when they can.</summary>
    public string? Ineligibility(string? employmentType, DateOnly? joined, DateOnly today)
    {
        if (!IsActive)
        {
            return "This plan is closed.";
        }

        if (EmploymentTypes.Count > 0 && (employmentType is null || !EmploymentTypes.Contains(employmentType, StringComparer.OrdinalIgnoreCase)))
        {
            return "This plan is not offered for your type of employment.";
        }

        if (MinServiceMonths > 0 && (joined is null || joined.Value.AddMonths(MinServiceMonths) > today))
        {
            return $"You can join after {MinServiceMonths} month(s) of service.";
        }

        return null;
    }

    public decimal EmployeeShare(int dependents) => EmployeeContribution + (DependentContribution * dependents);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string? Text(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

public sealed record PlanDetails(
    string Name,
    BenefitType Type,
    string? Provider,
    string? Description,
    decimal? CoverageAmount,
    string? Currency,
    decimal EmployeeContribution,
    decimal EmployerContribution,
    decimal DependentContribution,
    bool AllowsDependents,
    int MaxDependents,
    IReadOnlyList<string>? EligibleEmploymentTypes,
    int MinServiceMonths,
    bool AutoEnroll,
    bool Mandatory,
    bool RequiresApproval,
    bool EnrollmentOpen,
    bool EmployeeShareTaxDeductible);

public enum EnrollmentStatus
{
    Pending = 1,
    Active = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// One person's place in a plan (spec §24): from when, with which dependents, and what it costs each
/// month - the shares are fixed when they join, so a later price change starts with re-enrollment.
/// </summary>
public sealed class BenefitEnrollment : TenantAggregateRoot
{
    private BenefitEnrollment()
    {
        DependentIdsJson = "[]";
    }

    public Guid EmployeeId { get; private set; }

    public Guid PlanId { get; private set; }

    public string DependentIdsJson { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public decimal EmployeeMonthly { get; private set; }

    public decimal EmployerMonthly { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public string? Notes { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public IReadOnlyList<Guid> DependentIds => JsonSerializer.Deserialize<List<Guid>>(DependentIdsJson) ?? [];

    public static BenefitEnrollment Join(Guid employeeId, BenefitPlan plan, IReadOnlyList<Guid> dependents, DateOnly effectiveFrom, bool pending, string? notes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var enrollment = new BenefitEnrollment
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            PlanId = plan.Id,
            EffectiveFrom = effectiveFrom,
            Status = pending ? EnrollmentStatus.Pending : EnrollmentStatus.Active,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 500),
        };
        enrollment.Cover(plan, dependents);
        return enrollment;
    }

    /// <summary>Sets the dependents covered, and the employee's share that follows from them.</summary>
    public void Cover(BenefitPlan plan, IReadOnlyList<Guid> dependents)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dependents);
        if (Status is EnrollmentStatus.Cancelled or EnrollmentStatus.Rejected)
        {
            throw new DomainRuleException("ENROLLMENT_STATE", "This enrollment has ended.");
        }

        List<Guid> distinct = [.. dependents.Distinct()];
        if (distinct.Count > 0 && !plan.AllowsDependents)
        {
            throw new DomainRuleException("ENROLLMENT_DEPENDENTS", $"{plan.Name} covers the employee only.");
        }

        if (distinct.Count > plan.MaxDependents && plan.AllowsDependents)
        {
            throw new DomainRuleException("ENROLLMENT_DEPENDENTS", $"{plan.Name} covers up to {plan.MaxDependents} dependent(s).");
        }

        DependentIdsJson = JsonSerializer.Serialize(distinct);
        EmployeeMonthly = plan.EmployeeShare(distinct.Count);
        EmployerMonthly = plan.EmployerContribution;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool approved, DateTime now)
    {
        if (Status != EnrollmentStatus.Pending)
        {
            return;
        }

        Status = approved ? EnrollmentStatus.Active : EnrollmentStatus.Rejected;
        DecidedDate = now;
    }

    /// <summary>Coverage stops after <paramref name="lastDay"/>; a pending enrollment is simply withdrawn.</summary>
    public void End(DateOnly lastDay)
    {
        if (Status == EnrollmentStatus.Pending)
        {
            Status = EnrollmentStatus.Cancelled;
            return;
        }

        if (Status != EnrollmentStatus.Active)
        {
            throw new DomainRuleException("ENROLLMENT_STATE", "Only an active enrollment can end.");
        }

        if (lastDay < EffectiveFrom)
        {
            Status = EnrollmentStatus.Cancelled;
            EffectiveTo = EffectiveFrom.AddDays(-1);
            return;
        }

        EffectiveTo = lastDay;
    }

    public bool ActiveDuring(DateOnly first, DateOnly last) => Status == EnrollmentStatus.Active && EffectiveFrom <= last && (EffectiveTo is null || EffectiveTo >= first);

    /// <summary>The share of a period it was active for: whole months pay in full, part months pro rata.</summary>
    public decimal ShareOf(DateOnly first, DateOnly last)
    {
        if (!ActiveDuring(first, last))
        {
            return 0;
        }

        DateOnly from = EffectiveFrom > first ? EffectiveFrom : first;
        DateOnly to = EffectiveTo is { } end && end < last ? end : last;
        return (decimal)(to.DayNumber - from.DayNumber + 1) / (last.DayNumber - first.DayNumber + 1);
    }
}

/// <summary>The plans a tenant starts with - typical Indian employer cover, all editable.</summary>
public static class BenefitDefaults
{
    public static IReadOnlyList<(string Code, PlanDetails Details)> Plans { get; } =
    [
        ("GHI", new PlanDetails("Group health insurance", BenefitType.Health, null, "Hospitalisation cover for the employee and family.", 500000m, "INR",
            0m, 800m, 250m, AllowsDependents: true, MaxDependents: 5, null, 0, AutoEnroll: true, Mandatory: true, RequiresApproval: false, EnrollmentOpen: true, EmployeeShareTaxDeductible: true)),
        ("GTL", new PlanDetails("Group term life", BenefitType.Life, null, "Life cover of three times annual CTC, up to the sum assured.", 3000000m, "INR",
            0m, 150m, 0m, AllowsDependents: false, MaxDependents: 0, null, 0, AutoEnroll: true, Mandatory: true, RequiresApproval: false, EnrollmentOpen: true, EmployeeShareTaxDeductible: false)),
        ("GPA", new PlanDetails("Group personal accident", BenefitType.Accident, null, "Accidental death and disability cover.", 1000000m, "INR",
            0m, 50m, 0m, AllowsDependents: false, MaxDependents: 0, null, 0, AutoEnroll: true, Mandatory: true, RequiresApproval: false, EnrollmentOpen: true, EmployeeShareTaxDeductible: false)),
        ("WELLNESS", new PlanDetails("Wellness programme", BenefitType.Wellness, null, "Gym, counselling and annual health check-ups.", null, "INR",
            200m, 300m, 0m, AllowsDependents: false, MaxDependents: 0, null, 3, AutoEnroll: false, Mandatory: false, RequiresApproval: false, EnrollmentOpen: true, EmployeeShareTaxDeductible: false)),
    ];
}
