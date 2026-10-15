using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Resources.Domain;

/// <summary>
/// What resourcing knows about a person beyond Core HR (spec §58): what an hour costs and bills, how
/// many hours a week they work, their notice period, and whether they are billable at all.
/// </summary>
public sealed class ResourceProfile : TenantAggregateRoot
{
    private ResourceProfile()
    {
        Currency = null!;
    }

    public Guid EmployeeId { get; private set; }

    public decimal? CostRate { get; private set; }

    public decimal? BillingRate { get; private set; }

    public string Currency { get; private set; }

    public decimal CapacityHoursPerWeek { get; private set; }

    public int? NoticePeriodDays { get; private set; }

    /// <summary>Billable people are measured on utilization; support functions are not.</summary>
    public bool IsBillable { get; private set; }

    public string? Notes { get; private set; }

    public static ResourceProfile Create(Guid employeeId)
        => new() { EmployeeId = Guard.NotEmpty(employeeId), Currency = "INR", CapacityHoursPerWeek = 40, IsBillable = true };

    public void Update(decimal? costRate, decimal? billingRate, string currency, decimal capacityHoursPerWeek, int? noticePeriodDays, bool isBillable, string? notes)
    {
        if (costRate is < 0 || billingRate is < 0)
        {
            throw new DomainRuleException("RESOURCE_RATE", "Rates cannot be negative.");
        }

        if (capacityHoursPerWeek is <= 0 or > 80)
        {
            throw new DomainRuleException("RESOURCE_CAPACITY", "Weekly capacity must be between 1 and 80 hours.");
        }

        if (noticePeriodDays is < 0 or > 365)
        {
            throw new DomainRuleException("RESOURCE_NOTICE", "Notice period must be between 0 and 365 days.");
        }

        CostRate = costRate;
        BillingRate = billingRate;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 8);
        CapacityHoursPerWeek = capacityHoursPerWeek;
        NoticePeriodDays = noticePeriodDays;
        IsBillable = isBillable;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }

    public decimal HoursPerDay => CapacityHoursPerWeek / 5;
}

public enum RequestPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum RequestStatus
{
    Open = 1,
    PartiallyFulfilled = 2,
    Fulfilled = 3,
    Cancelled = 4,
}

/// <summary>A skill a request calls for, at a minimum level (1-5).</summary>
public sealed record SkillRequirement(Guid SkillId, int MinLevel);

public sealed record RequestDetails(
    Guid? ProjectRoleId,
    string Title,
    IReadOnlyList<SkillRequirement> Skills,
    decimal? MinYearsExperience,
    int Quantity,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Billable,
    Guid? LocationId,
    string? WorkModeCode,
    decimal? MaxCostRate,
    RequestPriority Priority,
    string? Notes);

/// <summary>
/// "Need 3 senior .NET developers from 1 December to 31 January at 100%" (spec §61): a project's ask
/// for people, filled one allocation at a time.
/// </summary>
public sealed class ResourceRequest : TenantAggregateRoot
{
    private ResourceRequest()
    {
        Title = null!;
        SkillsJson = "[]";
    }

    public Guid ProjectId { get; private set; }

    public Guid? ProjectRoleId { get; private set; }

    public string Title { get; private set; }

    public string SkillsJson { get; private set; }

    public decimal? MinYearsExperience { get; private set; }

    public int Quantity { get; private set; }

    public int FulfilledCount { get; private set; }

    public int AllocationPercent { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public bool Billable { get; private set; }

    public Guid? LocationId { get; private set; }

    public string? WorkModeCode { get; private set; }

    /// <summary>The most an hour of this person may cost the project.</summary>
    public decimal? MaxCostRate { get; private set; }

    public RequestPriority Priority { get; private set; }

    public Guid? RequestedByEmployeeId { get; private set; }

    public RequestStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<SkillRequirement> Skills => JsonSerializer.Deserialize<List<SkillRequirement>>(SkillsJson) ?? [];

    public int Remaining => Math.Max(0, Quantity - FulfilledCount);

    public bool IsOpen => Status is RequestStatus.Open or RequestStatus.PartiallyFulfilled;

    public static ResourceRequest Raise(Guid projectId, Guid? requestedBy, RequestDetails details)
    {
        var request = new ResourceRequest { ProjectId = Guard.NotEmpty(projectId), RequestedByEmployeeId = requestedBy, Status = RequestStatus.Open };
        request.Update(details);
        return request;
    }

    public void Update(RequestDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(details.Skills);

        if (Status is RequestStatus.Cancelled or RequestStatus.Fulfilled)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_CLOSED", $"This request is {Status.ToString().ToLowerInvariant()}.");
        }

        if (details.Quantity is < 1 or > 100 || details.Quantity < FulfilledCount)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_QUANTITY", "Ask for between 1 and 100 people, and not fewer than already found.");
        }

        if (details.AllocationPercent is < 1 or > 100)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_ALLOCATION", "Allocation is between 1 and 100 percent.");
        }

        if (details.EndDate < details.StartDate)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_DATES", "The request ends before it starts.");
        }

        if (details.Skills.Any(skill => skill.MinLevel is < 1 or > 5) || details.Skills.Select(skill => skill.SkillId).Distinct().Count() != details.Skills.Count)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_SKILLS", "Each skill appears once, at a level from 1 to 5.");
        }

        if (details.MinYearsExperience is < 0 or > 50 || details.MaxCostRate is < 0 || !Enum.IsDefined(details.Priority))
        {
            throw new DomainRuleException("RESOURCE_REQUEST_VALUE", "Experience, cost and priority must be sensible values.");
        }

        ProjectRoleId = details.ProjectRoleId;
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 200);
        SkillsJson = JsonSerializer.Serialize(details.Skills.ToList());
        MinYearsExperience = details.MinYearsExperience;
        Quantity = details.Quantity;
        AllocationPercent = details.AllocationPercent;
        StartDate = details.StartDate;
        EndDate = details.EndDate;
        Billable = details.Billable;
        LocationId = details.LocationId;
        WorkModeCode = string.IsNullOrWhiteSpace(details.WorkModeCode) ? null : details.WorkModeCode.Trim();
        MaxCostRate = details.MaxCostRate;
        Priority = details.Priority;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Guard.MaxLength(details.Notes.Trim(), 2000);
        RefreshStatus();
    }

    /// <summary>One more person allocated against the request.</summary>
    public void Fulfil()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_CLOSED", $"This request is {Status.ToString().ToLowerInvariant()}.");
        }

        FulfilledCount++;
        RefreshStatus();
    }

    public void Cancel()
    {
        if (Status == RequestStatus.Fulfilled)
        {
            throw new DomainRuleException("RESOURCE_REQUEST_CLOSED", "A fulfilled request cannot be cancelled.");
        }

        Status = RequestStatus.Cancelled;
    }

    private void RefreshStatus()
        => Status = FulfilledCount == 0 ? RequestStatus.Open : FulfilledCount >= Quantity ? RequestStatus.Fulfilled : RequestStatus.PartiallyFulfilled;
}

public enum CandidateStatus
{
    Proposed = 1,
    Allocated = 2,
    Rejected = 3,
}

/// <summary>Someone shortlisted for a request (spec §123 ResourceCandidate), with the score they were matched at.</summary>
public sealed class ResourceCandidate : TenantAggregateRoot
{
    private ResourceCandidate()
    {
        BreakdownJson = "{}";
    }

    public Guid RequestId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public decimal Score { get; private set; }

    public string BreakdownJson { get; private set; }

    public CandidateStatus Status { get; private set; }

    public Guid? AllocationMemberId { get; private set; }

    public string? Note { get; private set; }

    public static ResourceCandidate Shortlist(Guid requestId, Guid employeeId, MatchScore score, string? note) => new()
    {
        RequestId = Guard.NotEmpty(requestId),
        EmployeeId = Guard.NotEmpty(employeeId),
        Score = score.Overall,
        BreakdownJson = JsonSerializer.Serialize(score),
        Status = CandidateStatus.Proposed,
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 500),
    };

    public void Allocate(Guid memberId)
    {
        if (Status != CandidateStatus.Proposed)
        {
            throw new DomainRuleException("RESOURCE_CANDIDATE_STATE", $"This candidate is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = CandidateStatus.Allocated;
        AllocationMemberId = memberId;
    }

    public void Reject(string? note)
    {
        if (Status == CandidateStatus.Allocated)
        {
            throw new DomainRuleException("RESOURCE_CANDIDATE_STATE", "This candidate has been allocated; release the allocation instead.");
        }

        Status = CandidateStatus.Rejected;
        Note = string.IsNullOrWhiteSpace(note) ? Note : Guard.MaxLength(note.Trim(), 500);
    }

    public MatchScore? Breakdown => JsonSerializer.Deserialize<MatchScore>(BreakdownJson);
}

public enum TransferStatus
{
    Pending = 1,
    Completed = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// Moving someone from one project to another (spec §69): released here, allocated there, through
/// approval when the tenant has one - with both ends of the move kept in the allocation history.
/// </summary>
public sealed class ResourceTransfer : TenantAggregateRoot
{
    private ResourceTransfer()
    {
        Reason = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid FromMemberId { get; private set; }

    public Guid FromProjectId { get; private set; }

    public Guid ToProjectId { get; private set; }

    public Guid? ProjectRoleId { get; private set; }

    public int AllocationPercent { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public bool Billable { get; private set; }

    public string Reason { get; private set; }

    public TransferStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public Guid? NewMemberId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public static ResourceTransfer Request(
        Guid employeeId, Guid fromMemberId, Guid fromProjectId, Guid toProjectId, Guid? roleId, int allocationPercent, DateOnly effectiveDate, DateOnly? endDate, bool billable, string reason)
    {
        if (fromProjectId == toProjectId)
        {
            throw new DomainRuleException("RESOURCE_TRANSFER_SAME", "A transfer moves someone to a different project.");
        }

        if (allocationPercent is < 1 or > 100 || (endDate is { } end && end < effectiveDate))
        {
            throw new DomainRuleException("RESOURCE_TRANSFER_VALUE", "Allocation is 1-100% and the end follows the start.");
        }

        return new ResourceTransfer
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            FromMemberId = Guard.NotEmpty(fromMemberId),
            FromProjectId = fromProjectId,
            ToProjectId = toProjectId,
            ProjectRoleId = roleId,
            AllocationPercent = allocationPercent,
            EffectiveDate = effectiveDate,
            EndDate = endDate,
            Billable = billable,
            Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500),
            Status = TransferStatus.Pending,
        };
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Complete(Guid newMemberId, DateTime now)
    {
        EnsurePending();
        Status = TransferStatus.Completed;
        NewMemberId = newMemberId;
        DecidedDate = now;
    }

    public void Reject(DateTime now)
    {
        EnsurePending();
        Status = TransferStatus.Rejected;
        DecidedDate = now;
    }

    public void Cancel()
    {
        EnsurePending();
        Status = TransferStatus.Cancelled;
    }

    private void EnsurePending()
    {
        if (Status != TransferStatus.Pending)
        {
            throw new DomainRuleException("RESOURCE_TRANSFER_STATE", $"This transfer is already {Status.ToString().ToLowerInvariant()}.");
        }
    }
}
