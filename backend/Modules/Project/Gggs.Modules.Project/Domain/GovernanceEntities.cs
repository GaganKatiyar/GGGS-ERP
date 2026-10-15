using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

/// <summary>
/// A change request's life (spec §50): drafted with its impact analysed, approved through the
/// tenant's workflow (project manager, client sign-off), then implemented, tested and closed.
/// </summary>
public enum ChangeRequestStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Implementing = 5,
    Testing = 6,
    Closed = 7,
    Withdrawn = 8,
}

public sealed record ChangeRequestDetails(
    string Title,
    string Description,
    string? Reason,
    string? Impact,
    decimal? EstimatedHours,
    decimal? EstimatedCost,
    int? ScheduleImpactDays,
    WorkPriority Priority,
    Guid? RequestedByEmployeeId,
    string? RequestedByName);

public sealed class ChangeRequest : TenantAggregateRoot
{
    private ChangeRequest()
    {
        Code = null!;
        Title = null!;
        Description = null!;
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>The impact analysis: what it touches and what it risks.</summary>
    public string? Impact { get; private set; }

    public decimal? EstimatedHours { get; private set; }

    public decimal? EstimatedCost { get; private set; }

    /// <summary>Days the change adds to (or, negative, saves from) the schedule.</summary>
    public int? ScheduleImpactDays { get; private set; }

    public WorkPriority Priority { get; private set; }

    public Guid? RequestedByEmployeeId { get; private set; }

    /// <summary>Who asked, when it was someone at the client rather than an employee.</summary>
    public string? RequestedByName { get; private set; }

    public ChangeRequestStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public DateOnly? ClientApprovedOn { get; private set; }

    /// <summary>The client's sign-off: an email subject, a PO amendment number.</summary>
    public string? ClientApprovalReference { get; private set; }

    public DateOnly? ImplementationDate { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public static ChangeRequest Draft(Guid projectId, int number, ChangeRequestDetails details)
    {
        var request = new ChangeRequest { ProjectId = Guard.NotEmpty(projectId), Number = number, Code = $"CR-{number}", Status = ChangeRequestStatus.Draft };
        request.Edit(details);
        return request;
    }

    public void Edit(ChangeRequestDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (Status != ChangeRequestStatus.Draft)
        {
            throw new DomainRuleException("PROJECT_CR_FROZEN", $"{Code} is {Status}; only a draft can change.");
        }

        if (details.EstimatedHours is < 0 || details.EstimatedCost is < 0 || !Enum.IsDefined(details.Priority))
        {
            throw new DomainRuleException("PROJECT_CR_VALUE", "Estimates cannot be negative.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Description).Trim(), 8000);
        Reason = Project.Clean(details.Reason, 2000);
        Impact = Project.Clean(details.Impact, 4000);
        EstimatedHours = details.EstimatedHours;
        EstimatedCost = details.EstimatedCost;
        ScheduleImpactDays = details.ScheduleImpactDays;
        Priority = details.Priority;
        RequestedByEmployeeId = details.RequestedByEmployeeId;
        RequestedByName = Project.Clean(details.RequestedByName, 200);
    }

    public void SubmitForApproval(Guid workflowInstanceId)
    {
        EnsureStatus(ChangeRequestStatus.Draft, "submitted");
        Status = ChangeRequestStatus.PendingApproval;
        WorkflowInstanceId = workflowInstanceId;
    }

    public void Approve(DateTime now)
    {
        if (Status is not (ChangeRequestStatus.Draft or ChangeRequestStatus.PendingApproval))
        {
            throw new DomainRuleException("PROJECT_CR_STATE", $"{Code} is {Status} and cannot be approved.");
        }

        Status = ChangeRequestStatus.Approved;
        DecidedDate = now;
    }

    public void Reject(DateTime now)
    {
        if (Status == ChangeRequestStatus.PendingApproval)
        {
            Status = ChangeRequestStatus.Rejected;
            DecidedDate = now;
        }
    }

    public void ReturnToDraft()
    {
        if (Status == ChangeRequestStatus.PendingApproval)
        {
            Status = ChangeRequestStatus.Draft;
            WorkflowInstanceId = null;
        }
    }

    public void RecordClientApproval(DateOnly on, string? reference)
    {
        if (Status is not (ChangeRequestStatus.Approved or ChangeRequestStatus.Implementing or ChangeRequestStatus.Testing))
        {
            throw new DomainRuleException("PROJECT_CR_STATE", "Record the client's approval once the change is approved internally.");
        }

        ClientApprovedOn = on;
        ClientApprovalReference = Project.Clean(reference, 200);
    }

    /// <summary>Moves an approved change through implementation, testing and closure - one step at a time.</summary>
    public void Advance(ChangeRequestStatus to, DateOnly today)
    {
        bool allowed = (Status, to) switch
        {
            (ChangeRequestStatus.Approved, ChangeRequestStatus.Implementing) => true,
            (ChangeRequestStatus.Implementing, ChangeRequestStatus.Testing) => true,
            (ChangeRequestStatus.Testing, ChangeRequestStatus.Implementing) => true,
            (ChangeRequestStatus.Testing, ChangeRequestStatus.Closed) => true,
            (ChangeRequestStatus.Approved, ChangeRequestStatus.Closed) => true,
            _ => false,
        };

        if (!allowed)
        {
            throw new DomainRuleException("PROJECT_CR_STATE", $"{Code} cannot go from {Status} to {to}.");
        }

        if (to == ChangeRequestStatus.Implementing)
        {
            ImplementationDate ??= today;
        }

        if (to == ChangeRequestStatus.Closed)
        {
            ClosedOn = today;
        }

        Status = to;
    }

    public void Withdraw()
    {
        if (Status is not (ChangeRequestStatus.Draft or ChangeRequestStatus.PendingApproval))
        {
            throw new DomainRuleException("PROJECT_CR_STATE", $"{Code} is {Status} and can no longer be withdrawn.");
        }

        Status = ChangeRequestStatus.Withdrawn;
        WorkflowInstanceId = null;
    }

    public bool AwaitsDecision => Status is ChangeRequestStatus.Draft or ChangeRequestStatus.PendingApproval;

    public bool IsAccepted => Status is ChangeRequestStatus.Approved or ChangeRequestStatus.Implementing or ChangeRequestStatus.Testing or ChangeRequestStatus.Closed;

    private void EnsureStatus(ChangeRequestStatus expected, string verb)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("PROJECT_CR_STATE", $"{Code} is {Status} and cannot be {verb}.");
        }
    }
}

/// <summary>The four registers of the RAID log (spec §51-53).</summary>
public enum RaidKind
{
    Risk = 1,
    Assumption = 2,
    Issue = 3,
    Dependency = 4,
}

public enum RaidStatus
{
    Open = 1,
    InProgress = 2,

    /// <summary>A risk mitigated, an issue fixed, an assumption confirmed, a dependency delivered.</summary>
    Resolved = 3,
    Closed = 4,

    /// <summary>A risk the project knowingly carries.</summary>
    Accepted = 5,
}

public enum Severity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public sealed record RaidDetails(
    string Title,
    string? Description,
    string? Category,
    int? Probability,
    int? Impact,
    Severity? Severity,
    WorkPriority Priority,
    Guid? OwnerEmployeeId,
    Guid? AssigneeEmployeeId,
    DateOnly? DueDate,
    string? Mitigation,
    string? ContingencyPlan,
    string? RootCause,
    string? Resolution,
    RaidStatus Status);

/// <summary>
/// One entry in the RAID log. Risks are scored probability × impact (1-25); issues carry a
/// severity. One table, so the unified RAID view (spec §53) is a filter rather than a union.
/// </summary>
public sealed class RaidItem : TenantAggregateRoot
{
    private RaidItem()
    {
        Code = null!;
        Title = null!;
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string Code { get; private set; }

    public RaidKind Kind { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public string? Category { get; private set; }

    /// <summary>1 (rare) to 5 (almost certain). Risks only.</summary>
    public int? Probability { get; private set; }

    /// <summary>1 (negligible) to 5 (severe). Risks only.</summary>
    public int? Impact { get; private set; }

    /// <summary>Probability × impact, 1-25.</summary>
    public int? Score { get; private set; }

    public Severity? Severity { get; private set; }

    public WorkPriority Priority { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? AssigneeEmployeeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public string? Mitigation { get; private set; }

    public string? ContingencyPlan { get; private set; }

    public string? RootCause { get; private set; }

    public string? Resolution { get; private set; }

    public RaidStatus Status { get; private set; }

    public DateOnly? ResolvedOn { get; private set; }

    public static RaidItem Create(Guid projectId, int number, RaidKind kind, RaidDetails details, DateOnly today)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainRuleException("PROJECT_RAID_KIND", "Unknown RAID register.");
        }

        var item = new RaidItem
        {
            ProjectId = Guard.NotEmpty(projectId),
            Number = number,
            Kind = kind,
            Code = $"{PrefixOf(kind)}-{number}",
        };
        item.Update(details, today);
        return item;
    }

    public static string PrefixOf(RaidKind kind) => kind switch
    {
        RaidKind.Risk => "RSK",
        RaidKind.Assumption => "ASM",
        RaidKind.Issue => "ISS",
        _ => "DEP",
    };

    public void Update(RaidDetails details, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!Enum.IsDefined(details.Status) || !Enum.IsDefined(details.Priority) || (details.Severity is { } severity && !Enum.IsDefined(severity)))
        {
            throw new DomainRuleException("PROJECT_RAID_VALUE", "Unknown status, priority or severity.");
        }

        if (Kind == RaidKind.Risk)
        {
            if (details.Probability is not (>= 1 and <= 5) || details.Impact is not (>= 1 and <= 5))
            {
                throw new DomainRuleException("PROJECT_RISK_SCORE", "A risk needs a probability and an impact, each from 1 to 5.");
            }

            Probability = details.Probability;
            Impact = details.Impact;
            Score = details.Probability * details.Impact;
        }
        else
        {
            Probability = null;
            Impact = null;
            Score = null;
        }

        if (Kind == RaidKind.Issue && details.Severity is null)
        {
            throw new DomainRuleException("PROJECT_ISSUE_SEVERITY", "An issue needs a severity.");
        }

        if (details.Status == RaidStatus.Accepted && Kind != RaidKind.Risk)
        {
            throw new DomainRuleException("PROJECT_RAID_ACCEPTED", "Only a risk can be accepted.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        Description = Project.Clean(details.Description, 4000);
        Category = Project.Clean(details.Category, 100);
        Severity = Kind == RaidKind.Issue ? details.Severity : null;
        Priority = details.Priority;
        OwnerEmployeeId = details.OwnerEmployeeId;
        AssigneeEmployeeId = details.AssigneeEmployeeId;
        DueDate = details.DueDate;
        Mitigation = Project.Clean(details.Mitigation, 4000);
        ContingencyPlan = Project.Clean(details.ContingencyPlan, 4000);
        RootCause = Project.Clean(details.RootCause, 4000);
        Resolution = Project.Clean(details.Resolution, 4000);

        bool closing = details.Status is RaidStatus.Resolved or RaidStatus.Closed;
        ResolvedOn = closing ? ResolvedOn ?? today : null;
        Status = details.Status;
    }

    public bool IsOpen => Status is RaidStatus.Open or RaidStatus.InProgress;

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is { } due && due < today;
}

public enum MeetingType
{
    Kickoff = 1,
    StatusReview = 2,
    SprintPlanning = 3,
    SprintReview = 4,
    Retrospective = 5,
    Steering = 6,
    Client = 7,
    Other = 8,
}

public enum MeetingStatus
{
    Scheduled = 1,
    Held = 2,
    Cancelled = 3,
}

public sealed record MeetingDetails(
    string Title,
    MeetingType Type,
    DateOnly Date,
    TimeOnly? StartTime,
    int? DurationMinutes,
    string? Location,
    IReadOnlyCollection<Guid> ParticipantEmployeeIds,
    string? ExternalParticipants,
    string? Agenda,
    string? Minutes,
    Guid? OwnerEmployeeId,
    MeetingStatus Status);

/// <summary>A project meeting (spec §54): agenda before, minutes and actions after.</summary>
public sealed class ProjectMeeting : TenantAggregateRoot
{
    private ProjectMeeting()
    {
        Title = null!;
        ParticipantIdsJson = "[]";
    }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    public MeetingType Type { get; private set; }

    public DateOnly Date { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    public int? DurationMinutes { get; private set; }

    /// <summary>A room or a call link.</summary>
    public string? Location { get; private set; }

    public string ParticipantIdsJson { get; private set; }

    public string? ExternalParticipants { get; private set; }

    public string? Agenda { get; private set; }

    public string? Minutes { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public MeetingStatus Status { get; private set; }

    public IReadOnlyList<Guid> ParticipantEmployeeIds => JsonSerializer.Deserialize<List<Guid>>(ParticipantIdsJson) ?? [];

    public static ProjectMeeting Schedule(Guid projectId, MeetingDetails details)
    {
        var meeting = new ProjectMeeting { ProjectId = Guard.NotEmpty(projectId) };
        meeting.Update(details);
        return meeting;
    }

    public void Update(MeetingDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(details.ParticipantEmployeeIds);

        if (!Enum.IsDefined(details.Type) || !Enum.IsDefined(details.Status) || details.DurationMinutes is < 1 or > 1440)
        {
            throw new DomainRuleException("PROJECT_MEETING_VALUE", "Unknown meeting type or status, or a duration beyond a day.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 200);
        Type = details.Type;
        Date = details.Date;
        StartTime = details.StartTime;
        DurationMinutes = details.DurationMinutes;
        Location = Project.Clean(details.Location, 300);
        ParticipantIdsJson = JsonSerializer.Serialize(details.ParticipantEmployeeIds.Distinct().Take(200).ToList());
        ExternalParticipants = Project.Clean(details.ExternalParticipants, 1000);
        Agenda = Project.Clean(details.Agenda, 8000);
        Minutes = Project.Clean(details.Minutes, 16000);
        OwnerEmployeeId = details.OwnerEmployeeId;
        Status = details.Status;
    }
}

public enum ActionStatus
{
    Open = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>Something a meeting agreed someone would do (spec §122 MeetingAction).</summary>
public sealed class MeetingAction : TenantAggregateRoot
{
    private MeetingAction()
    {
        Description = null!;
    }

    public Guid ProjectId { get; private set; }

    public Guid MeetingId { get; private set; }

    public string Description { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public ActionStatus Status { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public static MeetingAction Create(Guid projectId, Guid meetingId, string description, Guid? ownerEmployeeId, DateOnly? dueDate)
    {
        var action = new MeetingAction { ProjectId = Guard.NotEmpty(projectId), MeetingId = Guard.NotEmpty(meetingId), Status = ActionStatus.Open };
        action.Update(description, ownerEmployeeId, dueDate, ActionStatus.Open, DateOnly.MinValue);
        return action;
    }

    public void Update(string description, Guid? ownerEmployeeId, DateOnly? dueDate, ActionStatus status, DateOnly today)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainRuleException("PROJECT_ACTION_STATUS", "Unknown action status.");
        }

        Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(description).Trim(), 1000);
        OwnerEmployeeId = ownerEmployeeId;
        DueDate = dueDate;
        CompletedOn = status == ActionStatus.Done ? CompletedOn ?? today : null;
        Status = status;
    }

    public bool IsOverdue(DateOnly today) => Status == ActionStatus.Open && DueDate is { } due && due < today;
}

/// <summary>A day's verdict on a project's health (spec §78), kept so the trend can be shown.</summary>
public sealed class ProjectHealthSnapshot : TenantAggregateRoot
{
    private ProjectHealthSnapshot()
    {
        Summary = null!;
    }

    public Guid ProjectId { get; private set; }

    public DateOnly AssessedOn { get; private set; }

    public HealthRating Schedule { get; private set; }

    public HealthRating Budget { get; private set; }

    public HealthRating Resources { get; private set; }

    public HealthRating Scope { get; private set; }

    public HealthRating Quality { get; private set; }

    public HealthRating Risk { get; private set; }

    public HealthRating Issues { get; private set; }

    public HealthRating ClientSatisfaction { get; private set; }

    public HealthRating Overall { get; private set; }

    public decimal Score { get; private set; }

    public decimal ProgressPercent { get; private set; }

    /// <summary>Why each dimension came out as it did, one reason per line.</summary>
    public string Summary { get; private set; }

    public static ProjectHealthSnapshot Record(Guid projectId, DateOnly on, HealthResult result)
    {
        var snapshot = new ProjectHealthSnapshot { ProjectId = Guard.NotEmpty(projectId), AssessedOn = on };
        snapshot.Refresh(result);
        return snapshot;
    }

    public void Refresh(HealthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Schedule = result.Schedule;
        Budget = result.Budget;
        Resources = result.Resources;
        Scope = result.Scope;
        Quality = result.Quality;
        Risk = result.Risk;
        Issues = result.Issues;
        ClientSatisfaction = result.ClientSatisfaction;
        Overall = result.Overall;
        Score = Math.Round(result.Score, 2);
        ProgressPercent = Math.Round(result.ProgressPercent, 1);
        Summary = Guard.MaxLength(string.Join('\n', result.Reasons), 4000);
    }
}

/// <summary>
/// The thresholds projects are judged by (spec §78: "thresholds must be configurable"). One row per
/// tenant, created with the defaults below the first time anyone asks.
/// </summary>
public sealed class ProjectHealthSettings : TenantAggregateRoot
{
    private ProjectHealthSettings()
    {
    }

    /// <summary>Elapsed time ahead of progress by this many points turns schedule amber.</summary>
    public int ScheduleAmberGap { get; private set; } = 10;

    public int ScheduleRedGap { get; private set; } = 25;

    /// <summary>A milestone this many days late turns schedule red; any late milestone turns it amber.</summary>
    public int MilestoneRedOverdueDays { get; private set; } = 14;

    /// <summary>A team member leaving within this many days, while the project goes on, turns resources amber.</summary>
    public int ResourceEndingSoonDays { get; private set; } = 14;

    public int ScopeAmberPendingChanges { get; private set; } = 2;

    public int ScopeRedPendingChanges { get; private set; } = 5;

    /// <summary>Approved changes adding this many days in total turn scope red.</summary>
    public int ScopeRedScheduleImpactDays { get; private set; } = 30;

    public int QualityAmberOpenBugs { get; private set; } = 5;

    public int QualityRedOpenBugs { get; private set; } = 15;

    /// <summary>An open risk scoring this high (of 25) turns risk amber.</summary>
    public int RiskAmberScore { get; private set; } = 8;

    public int RiskRedScore { get; private set; } = 15;

    public int IssueRedOverdueCount { get; private set; } = 3;

    /// <summary>Weighted average of the dimensions (green 0, amber 1, red 3) at which overall turns amber.</summary>
    public decimal OverallAmberScore { get; private set; } = 0.5m;

    public decimal OverallRedScore { get; private set; } = 1.5m;

    public static ProjectHealthSettings Defaults() => new();

    public void Update(ProjectHealthThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        static void Order(int amber, int red, string what)
        {
            if (amber < 0 || red < amber)
            {
                throw new DomainRuleException("PROJECT_HEALTH_THRESHOLD", $"The red threshold for {what} must be at or above the amber one, and neither negative.");
            }
        }

        Order(thresholds.ScheduleAmberGap, thresholds.ScheduleRedGap, "schedule");
        Order(thresholds.ScopeAmberPendingChanges, thresholds.ScopeRedPendingChanges, "scope");
        Order(thresholds.QualityAmberOpenBugs, thresholds.QualityRedOpenBugs, "quality");
        Order(thresholds.RiskAmberScore, thresholds.RiskRedScore, "risk");

        if (thresholds.OverallAmberScore <= 0 || thresholds.OverallRedScore < thresholds.OverallAmberScore || thresholds.OverallRedScore > 3)
        {
            throw new DomainRuleException("PROJECT_HEALTH_THRESHOLD", "Overall thresholds lie between 0 and 3, red at or above amber.");
        }

        if (thresholds.MilestoneRedOverdueDays < 1 || thresholds.ResourceEndingSoonDays < 0 || thresholds.ScopeRedScheduleImpactDays < 1 || thresholds.IssueRedOverdueCount < 1)
        {
            throw new DomainRuleException("PROJECT_HEALTH_THRESHOLD", "Day and count thresholds must be positive.");
        }

        ScheduleAmberGap = thresholds.ScheduleAmberGap;
        ScheduleRedGap = thresholds.ScheduleRedGap;
        MilestoneRedOverdueDays = thresholds.MilestoneRedOverdueDays;
        ResourceEndingSoonDays = thresholds.ResourceEndingSoonDays;
        ScopeAmberPendingChanges = thresholds.ScopeAmberPendingChanges;
        ScopeRedPendingChanges = thresholds.ScopeRedPendingChanges;
        ScopeRedScheduleImpactDays = thresholds.ScopeRedScheduleImpactDays;
        QualityAmberOpenBugs = thresholds.QualityAmberOpenBugs;
        QualityRedOpenBugs = thresholds.QualityRedOpenBugs;
        RiskAmberScore = thresholds.RiskAmberScore;
        RiskRedScore = thresholds.RiskRedScore;
        IssueRedOverdueCount = thresholds.IssueRedOverdueCount;
        OverallAmberScore = thresholds.OverallAmberScore;
        OverallRedScore = thresholds.OverallRedScore;
    }

    public ProjectHealthThresholds ToThresholds() => new(
        ScheduleAmberGap, ScheduleRedGap, MilestoneRedOverdueDays, ResourceEndingSoonDays, ScopeAmberPendingChanges, ScopeRedPendingChanges,
        ScopeRedScheduleImpactDays, QualityAmberOpenBugs, QualityRedOpenBugs, RiskAmberScore, RiskRedScore, IssueRedOverdueCount,
        OverallAmberScore, OverallRedScore);
}

public sealed record ProjectHealthThresholds(
    int ScheduleAmberGap,
    int ScheduleRedGap,
    int MilestoneRedOverdueDays,
    int ResourceEndingSoonDays,
    int ScopeAmberPendingChanges,
    int ScopeRedPendingChanges,
    int ScopeRedScheduleImpactDays,
    int QualityAmberOpenBugs,
    int QualityRedOpenBugs,
    int RiskAmberScore,
    int RiskRedScore,
    int IssueRedOverdueCount,
    decimal OverallAmberScore,
    decimal OverallRedScore)
{
    public static ProjectHealthThresholds Default { get; } = ProjectHealthSettings.Defaults().ToThresholds();
}
