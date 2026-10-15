using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

public enum ProjectType
{
    FixedPrice = 1,
    TimeAndMaterial = 2,
    Retainer = 3,
    Internal = 4,
    RnD = 5,
    Support = 6,
    ManagedService = 7,
    Implementation = 8,
    Consulting = 9,
}

/// <summary>Where a project is in its life (spec §40), from the first conversation to closure.</summary>
public enum ProjectStage
{
    Opportunity = 1,
    Proposal = 2,
    Won = 3,
    Planning = 4,
    Kickoff = 5,
    Execution = 6,
    Testing = 7,
    Uat = 8,
    Deployment = 9,
    Support = 10,
    Closure = 11,
}

/// <summary>Whether work is happening - independent of the stage, so a project can be on hold in any stage.</summary>
public enum ProjectStatus
{
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4,
}

public enum WorkPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum ProjectBillingType
{
    FixedPrice = 1,
    TimeAndMaterial = 2,
    Milestone = 3,
    Retainer = 4,
    NonBillable = 5,
}

public enum Confidentiality
{
    Public = 1,
    Internal = 2,
    Confidential = 3,
    Restricted = 4,
}

public enum DeliveryMethodology
{
    Waterfall = 1,
    Agile = 2,
    Hybrid = 3,
}

public enum HealthRating
{
    NotAssessed = 0,
    Green = 1,
    Amber = 2,
    Red = 3,
}

/// <summary>Everything about a project its managers set, in one value so create and update agree.</summary>
public sealed record ProjectProfile(
    string Name,
    ProjectType Type,
    string? Category,
    Guid? ClientId,
    Guid? ContractId,
    Guid? BusinessUnitId,
    Guid? DepartmentId,
    Guid? ProjectManagerEmployeeId,
    Guid? DeliveryManagerEmployeeId,
    Guid? AccountManagerEmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? PlannedEndDate,
    WorkPriority Priority,
    ProjectBillingType BillingType,
    string Currency,
    decimal? Budget,
    decimal? RevenueTarget,
    decimal? CostBudget,
    string? Description,
    Guid? LocationId,
    string? WorkModeCode,
    Confidentiality Confidentiality,
    DeliveryMethodology Methodology);

/// <summary>
/// A project (spec §39): the master record every task, sprint, risk and person-on-the-project hangs
/// off. Its numbered items (tasks, requirements, change requests, RAID entries) draw their numbers
/// from counters kept here, so codes such as <c>APOLLO-42</c> are unique within the project.
/// </summary>
public sealed class Project : TenantAggregateRoot
{
    private Project()
    {
        Code = null!;
        Name = null!;
        Currency = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public ProjectType Type { get; private set; }

    public string? Category { get; private set; }

    public Guid? ClientId { get; private set; }

    public Guid? ContractId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? ProjectManagerEmployeeId { get; private set; }

    public Guid? DeliveryManagerEmployeeId { get; private set; }

    public Guid? AccountManagerEmployeeId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>The committed end date.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>The current forecast, when it differs from the commitment.</summary>
    public DateOnly? PlannedEndDate { get; private set; }

    public DateOnly? ActualEndDate { get; private set; }

    public WorkPriority Priority { get; private set; }

    public ProjectStatus Status { get; private set; }

    public ProjectStage Stage { get; private set; }

    public HealthRating HealthStatus { get; private set; }

    public DateOnly? HealthAssessedOn { get; private set; }

    public ProjectBillingType BillingType { get; private set; }

    public string Currency { get; private set; }

    public decimal? Budget { get; private set; }

    public decimal? RevenueTarget { get; private set; }

    public decimal? CostBudget { get; private set; }

    public string? Description { get; private set; }

    public Guid? LocationId { get; private set; }

    public string? WorkModeCode { get; private set; }

    public Confidentiality Confidentiality { get; private set; }

    public DeliveryMethodology Methodology { get; private set; }

    /// <summary>The client's latest rating of the work, 1 (unhappy) to 5 (delighted).</summary>
    public byte? ClientSatisfaction { get; private set; }

    public DateOnly? ClientSatisfactionOn { get; private set; }

    public int TaskSequence { get; private set; }

    public int RequirementSequence { get; private set; }

    public int ChangeRequestSequence { get; private set; }

    public int RaidSequence { get; private set; }

    /// <summary>The date health is judged against: the forecast when there is one.</summary>
    public DateOnly TargetEndDate => PlannedEndDate ?? EndDate;

    public bool IsOpen => Status is ProjectStatus.Active or ProjectStatus.OnHold;

    public static Project Create(string code, ProjectProfile profile, ProjectStage stage)
    {
        var project = new Project
        {
            Code = NormalizeCode(code),
            Status = ProjectStatus.Active,
            Stage = Enum.IsDefined(stage) ? stage : ProjectStage.Planning,
            HealthStatus = HealthRating.NotAssessed,
        };
        project.Update(profile);
        return project;
    }

    public static string NormalizeCode(string code)
    {
        string normalized = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 16);
        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new DomainRuleException("PROJECT_CODE_FORMAT", "A project code uses letters, digits and underscores only - it prefixes every task code.");
        }

        return normalized;
    }

    public void Update(ProjectProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!Enum.IsDefined(profile.Type) || !Enum.IsDefined(profile.Priority) || !Enum.IsDefined(profile.BillingType)
            || !Enum.IsDefined(profile.Confidentiality) || !Enum.IsDefined(profile.Methodology))
        {
            throw new DomainRuleException("PROJECT_VALUE", "One of the project's options is not recognised.");
        }

        if (profile.EndDate < profile.StartDate || (profile.PlannedEndDate is { } planned && planned < profile.StartDate))
        {
            throw new DomainRuleException("PROJECT_DATES", "A project cannot end before it starts.");
        }

        if (profile.Budget is < 0 || profile.RevenueTarget is < 0 || profile.CostBudget is < 0)
        {
            throw new DomainRuleException("PROJECT_AMOUNT", "Budgets and targets cannot be negative.");
        }

        if (profile.ContractId is not null && profile.ClientId is null)
        {
            throw new DomainRuleException("PROJECT_CONTRACT_WITHOUT_CLIENT", "A project delivered under a contract needs that contract's client.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(profile.Name).Trim(), 200);
        Type = profile.Type;
        Category = Clean(profile.Category, 100);
        ClientId = profile.ClientId;
        ContractId = profile.ContractId;
        BusinessUnitId = profile.BusinessUnitId;
        DepartmentId = profile.DepartmentId;
        ProjectManagerEmployeeId = profile.ProjectManagerEmployeeId;
        DeliveryManagerEmployeeId = profile.DeliveryManagerEmployeeId;
        AccountManagerEmployeeId = profile.AccountManagerEmployeeId;
        StartDate = profile.StartDate;
        EndDate = profile.EndDate;
        PlannedEndDate = profile.PlannedEndDate;
        Priority = profile.Priority;
        BillingType = profile.BillingType;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(profile.Currency).Trim().ToUpperInvariant(), 8);
        Budget = profile.Budget;
        RevenueTarget = profile.RevenueTarget;
        CostBudget = profile.CostBudget;
        Description = Clean(profile.Description, 4000);
        LocationId = profile.LocationId;
        WorkModeCode = Clean(profile.WorkModeCode, 64);
        Confidentiality = profile.Confidentiality;
        Methodology = profile.Methodology;
    }

    /// <summary>Moves to another stage. Stages can be skipped or revisited - a lifecycle is a guide, not a cage.</summary>
    public ProjectStage MoveToStage(ProjectStage stage)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new DomainRuleException("PROJECT_STAGE", "Unknown project stage.");
        }

        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled)
        {
            throw new DomainRuleException("PROJECT_CLOSED", $"The project is {Status}; reopen it before changing its stage.");
        }

        ProjectStage previous = Stage;
        Stage = stage;
        return previous;
    }

    public ProjectStatus SetStatus(ProjectStatus status, DateOnly today)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainRuleException("PROJECT_STATUS", "Unknown project status.");
        }

        ProjectStatus previous = Status;
        Status = status;

        if (status is ProjectStatus.Completed or ProjectStatus.Cancelled)
        {
            ActualEndDate ??= today;
        }
        else
        {
            ActualEndDate = null;
        }

        return previous;
    }

    public void RecordHealth(HealthRating overall, DateOnly assessedOn)
    {
        HealthStatus = overall;
        HealthAssessedOn = assessedOn;
    }

    public void RateClientSatisfaction(byte score, DateOnly on)
    {
        if (score is < 1 or > 5)
        {
            throw new DomainRuleException("PROJECT_SATISFACTION", "Client satisfaction is a score from 1 to 5.");
        }

        ClientSatisfaction = score;
        ClientSatisfactionOn = on;
    }

    public int NextTaskNumber() => ++TaskSequence;

    public int NextRequirementNumber() => ++RequirementSequence;

    public int NextChangeRequestNumber() => ++ChangeRequestSequence;

    public int NextRaidNumber() => ++RaidSequence;

    internal static string? Clean(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}

/// <summary>A change of stage or status, with who made it and why (spec §122 ProjectStatusHistory).</summary>
public sealed class ProjectStatusHistory : TenantAggregateRoot
{
    private ProjectStatusHistory()
    {
        Kind = null!;
        FromValue = null!;
        ToValue = null!;
    }

    public Guid ProjectId { get; private set; }

    /// <summary><c>Stage</c> or <c>Status</c>.</summary>
    public string Kind { get; private set; }

    public string FromValue { get; private set; }

    public string ToValue { get; private set; }

    public string? Comment { get; private set; }

    public Guid? ChangedByEmployeeId { get; private set; }

    public DateTime ChangedDate { get; private set; }

    public static ProjectStatusHistory Record(Guid projectId, string kind, string from, string to, string? comment, Guid? changedBy, DateTime now) => new()
    {
        ProjectId = projectId,
        Kind = kind,
        FromValue = from,
        ToValue = to,
        Comment = Project.Clean(comment, 1000),
        ChangedByEmployeeId = changedBy,
        ChangedDate = now,
    };
}

/// <summary>A role people play on projects (spec §42), with the skills it calls for.</summary>
public sealed class ProjectRole : TenantAggregateRoot
{
    private ProjectRole()
    {
        Code = null!;
        Name = null!;
        SkillsJson = "[]";
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Skills the role calls for; matched against employee skills once the Skills module lands (M6).</summary>
    public string SkillsJson { get; private set; }

    public bool IsSystem { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyList<string> Skills => JsonSerializer.Deserialize<List<string>>(SkillsJson) ?? [];

    public static ProjectRole Create(string code, string name, string? description, IReadOnlyCollection<string> skills, int sortOrder, bool isSystem)
    {
        var role = new ProjectRole
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32),
            IsSystem = isSystem,
        };
        role.Update(name, description, skills, sortOrder);
        return role;
    }

    public void Update(string name, string? description, IReadOnlyCollection<string> skills, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Description = Project.Clean(description, 500);
        SkillsJson = JsonSerializer.Serialize(skills.Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => skill.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList());
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum TeamMemberStatus
{
    Active = 1,
    Released = 2,
}

public sealed record TeamAssignment(
    Guid ProjectRoleId,
    DateOnly StartDate,
    DateOnly? EndDate,
    int AllocationPercent,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    string? Notes);

/// <summary>Someone on a project (spec §41): their role, share of time, dates and rates.</summary>
public sealed class ProjectTeamMember : TenantAggregateRoot
{
    private ProjectTeamMember()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid ProjectRoleId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public int AllocationPercent { get; private set; }

    public bool Billable { get; private set; }

    /// <summary>What the client is charged per hour.</summary>
    public decimal? BillingRate { get; private set; }

    /// <summary>What an hour costs the company; project financials (M7) build on it.</summary>
    public decimal? CostRate { get; private set; }

    public TeamMemberStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public static ProjectTeamMember Add(Guid projectId, Guid employeeId, TeamAssignment assignment)
    {
        var member = new ProjectTeamMember
        {
            ProjectId = Guard.NotEmpty(projectId),
            EmployeeId = Guard.NotEmpty(employeeId),
            Status = TeamMemberStatus.Active,
        };
        member.Update(assignment);
        return member;
    }

    public void Update(TeamAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (assignment.AllocationPercent is < 1 or > 100)
        {
            throw new DomainRuleException("PROJECT_ALLOCATION", "Allocation is a share of someone's time, from 1 to 100 percent.");
        }

        if (assignment.EndDate is { } end && end < assignment.StartDate)
        {
            throw new DomainRuleException("PROJECT_TEAM_DATES", "The end date is before the start date.");
        }

        if (assignment.BillingRate is < 0 || assignment.CostRate is < 0)
        {
            throw new DomainRuleException("PROJECT_RATE", "Rates cannot be negative.");
        }

        ProjectRoleId = Guard.NotEmpty(assignment.ProjectRoleId);
        StartDate = assignment.StartDate;
        EndDate = assignment.EndDate;
        AllocationPercent = assignment.AllocationPercent;
        Billable = assignment.Billable;
        BillingRate = assignment.BillingRate;
        CostRate = assignment.CostRate;
        Notes = Project.Clean(assignment.Notes, 500);
    }

    /// <summary>
    /// Ends the allocation on <paramref name="on"/>. A release dated ahead (a transfer next month, a
    /// notice period) only sets the end date: the person is still on the project until then.
    /// </summary>
    public void Release(DateOnly on, DateOnly today)
    {
        if (Status == TeamMemberStatus.Released)
        {
            return;
        }

        EndDate = on < StartDate ? StartDate : on;
        if (EndDate < today)
        {
            Status = TeamMemberStatus.Released;
        }
    }

    /// <summary>On the project on <paramref name="day"/>.</summary>
    public bool IsOnProject(DateOnly day) => Status == TeamMemberStatus.Active && StartDate <= day && (EndDate is null || EndDate >= day);
}

public enum PhaseStatus
{
    NotStarted = 1,
    InProgress = 2,
    Completed = 3,
    OnHold = 4,
}

public sealed record PhasePlan(string Name, int SortOrder, DateOnly? StartDate, DateOnly? EndDate, Guid? OwnerEmployeeId, decimal? Budget, PhaseStatus Status, int Progress);

/// <summary>A stage of the plan (spec §43): design, build, test - each with dates, an owner and a budget.</summary>
public sealed class ProjectPhase : TenantAggregateRoot
{
    private ProjectPhase()
    {
        Name = null!;
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public decimal? Budget { get; private set; }

    public PhaseStatus Status { get; private set; }

    /// <summary>Percent complete as its owner reports it; tasks in the phase give the measured figure.</summary>
    public int Progress { get; private set; }

    public static ProjectPhase Create(Guid projectId, PhasePlan plan)
    {
        var phase = new ProjectPhase { ProjectId = Guard.NotEmpty(projectId) };
        phase.Update(plan);
        return phase;
    }

    public void Update(PhasePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.StartDate is { } start && plan.EndDate is { } end && end < start)
        {
            throw new DomainRuleException("PROJECT_PHASE_DATES", "The phase ends before it starts.");
        }

        if (plan.Progress is < 0 or > 100 || plan.Budget is < 0 || !Enum.IsDefined(plan.Status))
        {
            throw new DomainRuleException("PROJECT_PHASE_VALUE", "Progress is 0-100 and the budget cannot be negative.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(plan.Name).Trim(), 100);
        SortOrder = plan.SortOrder;
        StartDate = plan.StartDate;
        EndDate = plan.EndDate;
        OwnerEmployeeId = plan.OwnerEmployeeId;
        Budget = plan.Budget;
        Status = plan.Status;
        Progress = plan.Status == PhaseStatus.Completed ? 100 : plan.Progress;
    }
}

/// <summary>What a board column means, whatever it is called: the rules key off this, not the name.</summary>
public enum ColumnCategory
{
    Backlog = 1,
    Todo = 2,
    InProgress = 3,
    Done = 4,
}

/// <summary>A column on a project's board (spec §45). Each project configures its own.</summary>
public sealed class BoardColumn : TenantAggregateRoot
{
    private BoardColumn()
    {
        Name = null!;
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    public ColumnCategory Category { get; private set; }

    /// <summary>Work-in-progress limit: the board warns when a column holds more.</summary>
    public int? WipLimit { get; private set; }

    public static BoardColumn Create(Guid projectId, string name, ColumnCategory category, int sortOrder, int? wipLimit)
    {
        var column = new BoardColumn { ProjectId = Guard.NotEmpty(projectId) };
        column.Update(name, category, sortOrder, wipLimit);
        return column;
    }

    public void Update(string name, ColumnCategory category, int sortOrder, int? wipLimit)
    {
        if (!Enum.IsDefined(category) || wipLimit is < 1)
        {
            throw new DomainRuleException("PROJECT_COLUMN", "Unknown column category, or a WIP limit below one.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 50);
        Category = category;
        SortOrder = sortOrder;
        WipLimit = wipLimit;
    }

    /// <summary>The columns a new project starts with (spec §45).</summary>
    public static IReadOnlyList<(string Name, ColumnCategory Category)> Defaults { get; } =
    [
        ("Backlog", ColumnCategory.Backlog),
        ("To Do", ColumnCategory.Todo),
        ("In Progress", ColumnCategory.InProgress),
        ("Code Review", ColumnCategory.InProgress),
        ("Testing", ColumnCategory.InProgress),
        ("UAT", ColumnCategory.InProgress),
        ("Done", ColumnCategory.Done),
    ];
}
