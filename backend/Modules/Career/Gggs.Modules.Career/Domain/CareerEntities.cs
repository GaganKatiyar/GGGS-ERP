using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Career.Domain;

public enum CareerTrack
{
    Individual = 1,
    Management = 2,
}

/// <summary>A step on a career path: the role (by job or grade), how long people usually spend before it, and what it asks for.</summary>
public sealed record CareerStep(string Title, Guid? JobId, Guid? GradeId, int MinMonthsInRole, IReadOnlyList<string> Skills, string? Description);

/// <summary>A career path: the steps from one role to the next on an individual or management track.</summary>
public sealed class CareerPath : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CareerPath()
    {
        Name = null!;
        StepsJson = "[]";
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public CareerTrack Track { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public string StepsJson { get; private set; }

    public IReadOnlyList<CareerStep> Steps => JsonSerializer.Deserialize<List<CareerStep>>(StepsJson, Json) ?? [];

    public static CareerPath Create(string name, string? description, CareerTrack track, Guid? departmentId, IReadOnlyList<CareerStep> steps)
    {
        var path = new CareerPath();
        path.Update(name, description, track, departmentId, steps, active: true);
        return path;
    }

    public void Update(string name, string? description, CareerTrack track, Guid? departmentId, IReadOnlyList<CareerStep> steps, bool active)
    {
        List<CareerStep> cleaned = [.. (steps ?? []).Where(step => !string.IsNullOrWhiteSpace(step.Title)).Select(step => step with
        {
            Title = Guard.MaxLength(step.Title.Trim(), 120),
            Skills = [.. (step.Skills ?? []).Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => Guard.MaxLength(skill.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase)],
            Description = string.IsNullOrWhiteSpace(step.Description) ? null : Guard.MaxLength(step.Description.Trim(), 1000),
            MinMonthsInRole = Math.Clamp(step.MinMonthsInRole, 0, 240),
        })];
        if (cleaned.Count < 2)
        {
            throw new DomainRuleException("PATH_STEPS", "A career path has at least two steps.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 2000);
        Track = track;
        DepartmentId = departmentId;
        StepsJson = JsonSerializer.Serialize(cleaned, Json);
        IsActive = active;
    }

    /// <summary>
    /// Where someone in this job and grade stands on the path, or -1: the step naming their job, or
    /// failing that the last step by their grade alone.
    /// </summary>
    public int StepOf(Guid? jobId, Guid? gradeId)
    {
        List<CareerStep> steps = [.. Steps];
        int byJob = jobId is null ? -1 : steps.FindLastIndex(step => step.JobId == jobId);
        return byJob >= 0 ? byJob : gradeId is null ? -1 : steps.FindLastIndex(step => step.JobId is null && step.GradeId == gradeId);
    }
}

public enum PromotionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Applied = 5,
    Withdrawn = 6,
}

public sealed record PromotionDetails(
    string ProposedDesignation,
    Guid? ProposedJobId,
    Guid? ProposedGradeId,
    Guid? ProposedPositionId,
    decimal? ProposedAnnualCtc,
    DateOnly EffectiveDate,
    string Justification);

/// <summary>A promotion nomination: the proposed role and pay, why, approval through the workflow, and applying it on its date.</summary>
public sealed class PromotionNomination : TenantAggregateRoot
{
    private PromotionNomination()
    {
        ProposedDesignation = null!;
        Justification = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid? NominatedByEmployeeId { get; private set; }

    public string? CurrentDesignation { get; private set; }

    public decimal? CurrentAnnualCtc { get; private set; }

    public string ProposedDesignation { get; private set; }

    public Guid? ProposedJobId { get; private set; }

    public Guid? ProposedGradeId { get; private set; }

    public Guid? ProposedPositionId { get; private set; }

    public decimal? ProposedAnnualCtc { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public string Justification { get; private set; }

    /// <summary>The latest released rating when nominated.</summary>
    public int? Rating { get; private set; }

    public PromotionStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public DateOnly? AppliedOn { get; private set; }

    /// <summary>The CTC increase proposed, as a percentage of the current CTC.</summary>
    public decimal? IncreasePercent => CurrentAnnualCtc is > 0 && ProposedAnnualCtc is { } proposed ? Math.Round((proposed - CurrentAnnualCtc.Value) / CurrentAnnualCtc.Value * 100, 1) : null;

    public static PromotionNomination Draft(Guid employeeId, Guid? nominatedBy, string? currentDesignation, decimal? currentCtc, int? rating, PromotionDetails details)
    {
        var nomination = new PromotionNomination
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            NominatedByEmployeeId = nominatedBy,
            CurrentDesignation = currentDesignation,
            CurrentAnnualCtc = currentCtc,
            Rating = rating,
            Status = PromotionStatus.Draft,
        };
        nomination.Update(details);
        return nomination;
    }

    public void Update(PromotionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is not (PromotionStatus.Draft or PromotionStatus.Rejected))
        {
            throw new DomainRuleException("PROMOTION_STATE", "Only a draft or rejected nomination can change.");
        }

        if (details.ProposedJobId is null && details.ProposedGradeId is null && details.ProposedPositionId is null && string.IsNullOrWhiteSpace(details.ProposedDesignation))
        {
            throw new DomainRuleException("PROMOTION_TARGET", "Say what the promotion is to: a job, grade, position or designation.");
        }

        if (details.ProposedAnnualCtc is <= 0)
        {
            throw new DomainRuleException("PROMOTION_CTC", "The proposed CTC must be above zero.");
        }

        if (CurrentAnnualCtc is { } current && details.ProposedAnnualCtc is { } proposed && proposed < current)
        {
            throw new DomainRuleException("PROMOTION_CTC", "A promotion does not reduce pay.");
        }

        ProposedDesignation = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.ProposedDesignation).Trim(), 200);
        ProposedJobId = details.ProposedJobId;
        ProposedGradeId = details.ProposedGradeId;
        ProposedPositionId = details.ProposedPositionId;
        ProposedAnnualCtc = details.ProposedAnnualCtc;
        EffectiveDate = details.EffectiveDate;
        Justification = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Justification).Trim(), 4000);
        Status = PromotionStatus.Draft;
    }

    public void Submit()
    {
        if (Status is not (PromotionStatus.Draft or PromotionStatus.Rejected))
        {
            throw new DomainRuleException("PROMOTION_STATE", "Only a draft nomination can be submitted.");
        }

        Status = PromotionStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool? approved, DateTime now)
    {
        if (Status != PromotionStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => PromotionStatus.Approved,
            false => PromotionStatus.Rejected,
            null => PromotionStatus.Draft,
        };
        DecidedDate = now;
    }

    public bool IsDue(DateOnly today) => Status == PromotionStatus.Approved && EffectiveDate <= today;

    public void MarkApplied(DateOnly today)
    {
        if (Status != PromotionStatus.Approved)
        {
            throw new DomainRuleException("PROMOTION_STATE", "Only an approved promotion can be applied.");
        }

        Status = PromotionStatus.Applied;
        AppliedOn = today;
    }

    public void Withdraw()
    {
        if (Status is PromotionStatus.Applied or PromotionStatus.Withdrawn)
        {
            throw new DomainRuleException("PROMOTION_STATE", $"The nomination is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = PromotionStatus.Withdrawn;
    }
}

public enum Criticality
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum Readiness
{
    ReadyNow = 1,
    OneToTwoYears = 2,
    ThreeToFiveYears = 3,
}

public sealed record SuccessorEntry(Guid EmployeeId, Readiness Readiness, string? Notes);

/// <summary>A succession plan for a key role: who holds it, how critical it is, how likely they are to leave, and who could step in.</summary>
public sealed class SuccessionPlan : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private SuccessionPlan()
    {
        RoleTitle = null!;
        SuccessorsJson = "[]";
    }

    public string RoleTitle { get; private set; }

    public Guid? PositionId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? IncumbentEmployeeId { get; private set; }

    public Criticality Criticality { get; private set; }

    /// <summary>The risk the role falls vacant: the incumbent's flight risk or retirement.</summary>
    public Criticality VacancyRisk { get; private set; }

    public string? Notes { get; private set; }

    public string SuccessorsJson { get; private set; }

    public IReadOnlyList<SuccessorEntry> Successors => JsonSerializer.Deserialize<List<SuccessorEntry>>(SuccessorsJson, Json) ?? [];

    /// <summary>Whether someone is ready to step in now.</summary>
    public bool Covered => Successors.Any(successor => successor.Readiness == Readiness.ReadyNow);

    public static SuccessionPlan Create(string roleTitle, Guid? positionId, Guid? departmentId, Guid? incumbent, Criticality criticality, Criticality vacancyRisk, string? notes, IReadOnlyList<SuccessorEntry> successors)
    {
        var plan = new SuccessionPlan();
        plan.Update(roleTitle, positionId, departmentId, incumbent, criticality, vacancyRisk, notes, successors);
        return plan;
    }

    public void Update(string roleTitle, Guid? positionId, Guid? departmentId, Guid? incumbent, Criticality criticality, Criticality vacancyRisk, string? notes, IReadOnlyList<SuccessorEntry> successors)
    {
        List<SuccessorEntry> list = [.. (successors ?? []).Where(entry => entry.EmployeeId != Guid.Empty)
            .GroupBy(entry => entry.EmployeeId).Select(group => group.First())
            .Select(entry => entry with { Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : Guard.MaxLength(entry.Notes.Trim(), 500) })];
        if (incumbent is { } holder && list.Exists(entry => entry.EmployeeId == holder))
        {
            throw new DomainRuleException("SUCCESSION_SELF", "The incumbent cannot be their own successor.");
        }

        if (list.Count > 10)
        {
            throw new DomainRuleException("SUCCESSION_LIMIT", "Name up to ten successors.");
        }

        RoleTitle = Guard.MaxLength(Guard.NotNullOrWhiteSpace(roleTitle).Trim(), 200);
        PositionId = positionId;
        DepartmentId = departmentId;
        IncumbentEmployeeId = incumbent;
        Criticality = criticality;
        VacancyRisk = vacancyRisk;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 2000);
        SuccessorsJson = JsonSerializer.Serialize(list.OrderBy(entry => entry.Readiness).ToList(), Json);
    }
}

public enum Level
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>A manager's or HR's view of someone's potential and flight risk, for the talent review.</summary>
public sealed class TalentAssessment : TenantAggregateRoot
{
    private TalentAssessment()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Level Potential { get; private set; }

    public Level FlightRisk { get; private set; }

    public string? Notes { get; private set; }

    public Guid? AssessedByEmployeeId { get; private set; }

    public DateOnly AssessedOn { get; private set; }

    public static TalentAssessment Record(Guid employeeId, Level potential, Level flightRisk, string? notes, Guid? assessedBy, DateOnly today) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        Potential = potential,
        FlightRisk = flightRisk,
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000),
        AssessedByEmployeeId = assessedBy,
        AssessedOn = today,
    };
}

/// <summary>The 9-box: performance (from the latest rating) against potential, each low, medium or high.</summary>
public static class NineBox
{
    /// <summary>Ratings 1-2 are low performance, 3 medium, 4-5 high.</summary>
    public static Level PerformanceBand(int rating) => rating switch
    {
        <= 2 => Level.Low,
        3 => Level.Medium,
        _ => Level.High,
    };

    /// <summary>The box, 1 (low, low) to 9 (high, high), rows by potential, columns by performance.</summary>
    public static int Box(Level performance, Level potential) => ((int)potential - 1) * 3 + (int)performance;

    public static string Label(int box) => box switch
    {
        1 => "Underperformer",
        2 => "Effective",
        3 => "Trusted professional",
        4 => "Inconsistent",
        5 => "Core player",
        6 => "High performer",
        7 => "Rough diamond",
        8 => "Emerging star",
        9 => "Star",
        _ => "Unrated",
    };
}
