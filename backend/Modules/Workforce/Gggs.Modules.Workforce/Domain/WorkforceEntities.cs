using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Workforce.Domain;

public enum PlanStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Closed = 5,
}

/// <summary>
/// A workforce plan for a year (spec §11): how many people each department, location and job should
/// have, and the salary and hiring budget behind that - approved before hiring against it.
/// </summary>
public sealed class WorkforcePlan : TenantAggregateRoot
{
    private WorkforcePlan()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public int Year { get; private set; }

    public PlanStatus Status { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public string? Notes { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public static WorkforcePlan Draft(string name, int year, DateOnly effectiveDate, Guid? ownerEmployeeId, string? notes)
    {
        var plan = new WorkforcePlan { Status = PlanStatus.Draft };
        plan.Update(name, year, effectiveDate, ownerEmployeeId, notes);
        return plan;
    }

    public void Update(string name, int year, DateOnly effectiveDate, Guid? ownerEmployeeId, string? notes)
    {
        EnsureEditable();
        if (year is < 2000 or > 2100)
        {
            throw new DomainRuleException("PLAN_YEAR", "Give the plan a year between 2000 and 2100.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Year = year;
        EffectiveDate = effectiveDate;
        OwnerEmployeeId = ownerEmployeeId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 2000);
    }

    public void EnsureEditable()
    {
        if (Status is not (PlanStatus.Draft or PlanStatus.Rejected))
        {
            throw new DomainRuleException("PLAN_STATE", $"A {Status.ToString().ToLowerInvariant()} plan cannot change; close it and draft the next.");
        }
    }

    public void Submit()
    {
        if (Status is not (PlanStatus.Draft or PlanStatus.Rejected))
        {
            throw new DomainRuleException("PLAN_STATE", "Only a draft plan can be submitted.");
        }

        Status = PlanStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool? approved, DateTime now)
    {
        if (Status != PlanStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => PlanStatus.Approved,
            false => PlanStatus.Rejected,
            _ => PlanStatus.Draft,
        };
        DecidedDate = now;
    }

    public void Close()
    {
        if (Status != PlanStatus.Approved)
        {
            throw new DomainRuleException("PLAN_STATE", "Only an approved plan can be closed.");
        }

        Status = PlanStatus.Closed;
    }
}

/// <summary>One line of a plan: a department (optionally a location and job), its headcount today and planned, and the budgets.</summary>
public sealed class WorkforcePlanLine : TenantAggregateRoot
{
    private WorkforcePlanLine()
    {
    }

    public Guid PlanId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? LocationId { get; private set; }

    public Guid? JobId { get; private set; }

    /// <summary>Headcount when the line was last refreshed; the live figure is shown beside it.</summary>
    public int CurrentHeadcount { get; private set; }

    public int PlannedHeadcount { get; private set; }

    public decimal SalaryBudget { get; private set; }

    public decimal HiringBudget { get; private set; }

    public string? Notes { get; private set; }

    public int HeadcountGap => PlannedHeadcount - CurrentHeadcount;

    public static WorkforcePlanLine For(Guid planId, PlanLineDetails details, int currentHeadcount)
    {
        var line = new WorkforcePlanLine { PlanId = Guard.NotEmpty(planId) };
        line.Update(details, currentHeadcount);
        return line;
    }

    public void Update(PlanLineDetails details, int currentHeadcount)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.PlannedHeadcount is < 0 or > 100_000 || details.SalaryBudget < 0 || details.HiringBudget < 0)
        {
            throw new DomainRuleException("PLAN_LINE", "Headcount and budgets cannot be negative.");
        }

        DepartmentId = Guard.NotEmpty(details.DepartmentId);
        LocationId = details.LocationId;
        JobId = details.JobId;
        CurrentHeadcount = currentHeadcount;
        PlannedHeadcount = details.PlannedHeadcount;
        SalaryBudget = Math.Round(details.SalaryBudget, 2);
        HiringBudget = Math.Round(details.HiringBudget, 2);
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Guard.MaxLength(details.Notes.Trim(), 500);
    }

    public void Refresh(int currentHeadcount) => CurrentHeadcount = currentHeadcount;
}

public sealed record PlanLineDetails(Guid DepartmentId, Guid? LocationId, Guid? JobId, int PlannedHeadcount, decimal SalaryBudget, decimal HiringBudget, string? Notes);

public enum RequestReason
{
    NewRole = 1,
    Replacement = 2,
    Expansion = 3,
    Project = 4,
}

public enum RequestPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum ManpowerRequestStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Fulfilled = 6,
}

/// <summary>
/// A manpower request (spec §11): a manager asks for people - how many, with what skills and
/// experience, at what cost and by when - approved through the workflow before recruitment starts.
/// </summary>
public sealed class ManpowerRequest : TenantAggregateRoot
{
    private ManpowerRequest()
    {
        Number = null!;
        Title = null!;
        EmploymentTypeCode = null!;
        WorkModeCode = null!;
        SkillsJson = "[]";
    }

    public string Number { get; private set; }

    public string Title { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? PositionId { get; private set; }

    public Guid? JobId { get; private set; }

    public Guid? LocationId { get; private set; }

    public int Positions { get; private set; }

    public string SkillsJson { get; private set; }

    public decimal? MinExperienceYears { get; private set; }

    public decimal? MaxExperienceYears { get; private set; }

    public string EmploymentTypeCode { get; private set; }

    public string WorkModeCode { get; private set; }

    public decimal? SalaryMin { get; private set; }

    public decimal? SalaryMax { get; private set; }

    public decimal? Budget { get; private set; }

    public RequestPriority Priority { get; private set; }

    public DateOnly? RequiredBy { get; private set; }

    public RequestReason Reason { get; private set; }

    public Guid? ReplacementForEmployeeId { get; private set; }

    public string? Justification { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    public ManpowerRequestStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public IReadOnlyList<string> Skills => JsonSerializer.Deserialize<List<string>>(SkillsJson) ?? [];

    public static ManpowerRequest Raise(string number, Guid requesterEmployeeId, RequestDetails details)
    {
        var request = new ManpowerRequest
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30),
            RequesterEmployeeId = Guard.NotEmpty(requesterEmployeeId),
            Status = ManpowerRequestStatus.Draft,
        };
        request.Update(details);
        return request;
    }

    public void Update(RequestDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is not (ManpowerRequestStatus.Draft or ManpowerRequestStatus.Rejected))
        {
            throw new DomainRuleException("REQUEST_STATE", "Only a draft or rejected request can be changed.");
        }

        if (details.Positions is < 1 or > 500)
        {
            throw new DomainRuleException("REQUEST_POSITIONS", "Ask for between 1 and 500 people.");
        }

        if (details.SalaryMin > details.SalaryMax || details.MinExperienceYears > details.MaxExperienceYears)
        {
            throw new DomainRuleException("REQUEST_RANGE", "The minimum cannot be above the maximum.");
        }

        if (details.Reason == RequestReason.Replacement && details.ReplacementForEmployeeId is null)
        {
            throw new DomainRuleException("REQUEST_REPLACEMENT", "Say who the replacement is for.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 200);
        DepartmentId = Guard.NotEmpty(details.DepartmentId);
        PositionId = details.PositionId;
        JobId = details.JobId;
        LocationId = details.LocationId;
        Positions = details.Positions;
        SkillsJson = JsonSerializer.Serialize((details.Skills ?? []).Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => Guard.MaxLength(skill.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        MinExperienceYears = details.MinExperienceYears;
        MaxExperienceYears = details.MaxExperienceYears;
        EmploymentTypeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.EmploymentTypeCode).Trim().ToUpperInvariant(), 64);
        WorkModeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.WorkModeCode).Trim().ToUpperInvariant(), 64);
        SalaryMin = details.SalaryMin;
        SalaryMax = details.SalaryMax;
        Budget = details.Budget;
        Priority = details.Priority;
        RequiredBy = details.RequiredBy;
        Reason = details.Reason;
        ReplacementForEmployeeId = details.Reason == RequestReason.Replacement ? details.ReplacementForEmployeeId : null;
        Justification = string.IsNullOrWhiteSpace(details.Justification) ? null : Guard.MaxLength(details.Justification.Trim(), 2000);
    }

    public void Submit()
    {
        if (Status is not (ManpowerRequestStatus.Draft or ManpowerRequestStatus.Rejected))
        {
            throw new DomainRuleException("REQUEST_STATE", "Only a draft request can be submitted.");
        }

        Status = ManpowerRequestStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool? approved, DateTime now)
    {
        if (Status != ManpowerRequestStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => ManpowerRequestStatus.Approved,
            false => ManpowerRequestStatus.Rejected,
            _ => ManpowerRequestStatus.Draft,
        };
        DecidedDate = now;
    }

    public void Fulfil() => Status = Status == ManpowerRequestStatus.Approved ? ManpowerRequestStatus.Fulfilled : Status;

    public void Cancel()
    {
        if (Status is ManpowerRequestStatus.Fulfilled or ManpowerRequestStatus.Cancelled)
        {
            throw new DomainRuleException("REQUEST_STATE", $"A {Status.ToString().ToLowerInvariant()} request cannot be cancelled.");
        }

        Status = ManpowerRequestStatus.Cancelled;
    }
}

public sealed record RequestDetails(
    string Title,
    Guid DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? LocationId,
    int Positions,
    IReadOnlyList<string>? Skills,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears,
    string EmploymentTypeCode,
    string WorkModeCode,
    decimal? SalaryMin,
    decimal? SalaryMax,
    decimal? Budget,
    RequestPriority Priority,
    DateOnly? RequiredBy,
    RequestReason Reason,
    Guid? ReplacementForEmployeeId,
    string? Justification);
