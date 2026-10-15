using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

public enum TaskType
{
    Epic = 1,
    Story = 2,
    Task = 3,
    Subtask = 4,
    Bug = 5,
    ChangeRequest = 6,
}

public sealed record TaskDetails(
    TaskType Type,
    Guid? ParentTaskId,
    Guid? PhaseId,
    Guid? MilestoneId,
    Guid? RequirementId,
    string Title,
    string? Description,
    Guid? AssigneeEmployeeId,
    WorkPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    int? StoryPoints,
    int Progress,
    bool Billable);

/// <summary>
/// A piece of work (spec §44): an epic, story, task, subtask, bug or change. Where it sits on the
/// board is its column; its rank orders it within the column.
/// </summary>
public sealed class ProjectTask : TenantAggregateRoot
{
    private ProjectTask()
    {
        Code = null!;
        Title = null!;
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    /// <summary><c>PROJECTCODE-42</c>: what people say out loud.</summary>
    public string Code { get; private set; }

    public TaskType Type { get; private set; }

    public Guid? ParentTaskId { get; private set; }

    public Guid? PhaseId { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public Guid? SprintId { get; private set; }

    public Guid? RequirementId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public Guid? AssigneeEmployeeId { get; private set; }

    public Guid? ReporterEmployeeId { get; private set; }

    public WorkPriority Priority { get; private set; }

    public Guid ColumnId { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public decimal? EstimatedHours { get; private set; }

    /// <summary>Hours spent, as entered. Timesheets (M6) take this over.</summary>
    public decimal? ActualHours { get; private set; }

    public int? StoryPoints { get; private set; }

    public int Progress { get; private set; }

    public bool Billable { get; private set; }

    /// <summary>Order within the column: lower first. Moves pick a value between neighbours.</summary>
    public double Rank { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public bool IsDone => CompletedDate is not null;

    public static ProjectTask Create(
        Guid projectId,
        int number,
        string projectCode,
        TaskDetails details,
        BoardColumn column,
        double rank,
        Guid? reporterEmployeeId,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(column);

        var task = new ProjectTask
        {
            ProjectId = Guard.NotEmpty(projectId),
            Number = number,
            Code = $"{projectCode}-{number}",
            ReporterEmployeeId = reporterEmployeeId,
        };
        task.Update(details);
        task.MoveTo(column, rank, now);
        return task;
    }

    public void Update(TaskDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!Enum.IsDefined(details.Type) || !Enum.IsDefined(details.Priority))
        {
            throw new DomainRuleException("PROJECT_TASK_VALUE", "Unknown task type or priority.");
        }

        if (details.StartDate is { } start && details.DueDate is { } due && due < start)
        {
            throw new DomainRuleException("PROJECT_TASK_DATES", "The task is due before it starts.");
        }

        if (details.EstimatedHours is < 0 or > 10_000 || details.ActualHours is < 0 or > 10_000)
        {
            throw new DomainRuleException("PROJECT_TASK_HOURS", "Hours must be between 0 and 10,000.");
        }

        if (details.StoryPoints is < 0 or > 1000 || details.Progress is < 0 or > 100)
        {
            throw new DomainRuleException("PROJECT_TASK_VALUE", "Story points are 0-1000 and progress 0-100.");
        }

        if (details.ParentTaskId == Id && Id != Guid.Empty)
        {
            throw new DomainRuleException("PROJECT_TASK_PARENT", "A task cannot be its own parent.");
        }

        Type = details.Type;
        ParentTaskId = details.ParentTaskId;
        PhaseId = details.PhaseId;
        MilestoneId = details.MilestoneId;
        RequirementId = details.RequirementId;
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        Description = Project.Clean(details.Description, 8000);
        AssigneeEmployeeId = details.AssigneeEmployeeId;
        Priority = details.Priority;
        StartDate = details.StartDate;
        DueDate = details.DueDate;
        EstimatedHours = details.EstimatedHours;
        ActualHours = details.ActualHours;
        StoryPoints = details.StoryPoints;
        Progress = IsDone ? 100 : details.Progress;
        Billable = details.Billable;
    }

    /// <summary>Moves to a column. Arriving in a done column completes the task; leaving one reopens it.</summary>
    public void MoveTo(BoardColumn column, double rank, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.ProjectId != ProjectId)
        {
            throw new DomainRuleException("PROJECT_COLUMN_FOREIGN", "That column belongs to another project's board.");
        }

        ColumnId = column.Id;
        Rank = rank;

        if (column.Category == ColumnCategory.Done)
        {
            CompletedDate ??= now;
            Progress = 100;
        }
        else if (CompletedDate is not null)
        {
            CompletedDate = null;
            Progress = Math.Min(Progress, 90);
        }
    }

    public void PlanInto(Guid? sprintId) => SprintId = sprintId;

    /// <summary>Approved timesheet hours land here (or come off, when a timesheet is reopened).</summary>
    public void AddActualHours(decimal hours) => ActualHours = Math.Max(0, (ActualHours ?? 0) + hours);

    public bool IsOverdue(DateOnly today) => !IsDone && DueDate is { } due && due < today;
}

/// <summary>The rules for what may sit under what (spec §44): epics hold stories, stories hold tasks, tasks hold subtasks.</summary>
public static class TaskHierarchy
{
    public static bool CanHaveParent(TaskType child, TaskType? parent) => (child, parent) switch
    {
        (TaskType.Epic, null) => true,
        (TaskType.Epic, _) => false,
        (TaskType.Story, null or TaskType.Epic) => true,
        (TaskType.Task or TaskType.Bug or TaskType.ChangeRequest, null or TaskType.Epic or TaskType.Story) => true,
        (TaskType.Subtask, TaskType.Story or TaskType.Task or TaskType.Bug or TaskType.ChangeRequest) => true,
        _ => false,
    };

    public static void Ensure(TaskType child, TaskType? parent)
    {
        if (!CanHaveParent(child, parent))
        {
            string where = parent is null ? "on its own" : $"under a {Describe(parent.Value)}";
            throw new DomainRuleException("PROJECT_TASK_HIERARCHY", $"A {Describe(child)} cannot sit {where}.");
        }
    }

    public static string Describe(TaskType type) => type switch
    {
        TaskType.ChangeRequest => "change request",
        _ => type.ToString().ToLowerInvariant(),
    };
}

public enum DependencyType
{
    FinishToStart = 1,
    StartToStart = 2,
    FinishToFinish = 3,
    StartToFinish = 4,
}

/// <summary>One task waiting on another (spec §47).</summary>
public sealed class TaskDependency : TenantAggregateRoot
{
    private TaskDependency()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid PredecessorTaskId { get; private set; }

    public Guid SuccessorTaskId { get; private set; }

    public DependencyType Type { get; private set; }

    /// <summary>Days of slack (or overlap, when negative) between the two.</summary>
    public int LagDays { get; private set; }

    public static TaskDependency Create(Guid projectId, Guid predecessorId, Guid successorId, DependencyType type, int lagDays)
    {
        if (predecessorId == successorId)
        {
            throw new DomainRuleException("PROJECT_DEPENDENCY_SELF", "A task cannot depend on itself.");
        }

        if (!Enum.IsDefined(type) || lagDays is < -365 or > 365)
        {
            throw new DomainRuleException("PROJECT_DEPENDENCY_VALUE", "Unknown dependency type, or a lag beyond a year.");
        }

        return new TaskDependency
        {
            ProjectId = Guard.NotEmpty(projectId),
            PredecessorTaskId = Guard.NotEmpty(predecessorId),
            SuccessorTaskId = Guard.NotEmpty(successorId),
            Type = type,
            LagDays = lagDays,
        };
    }
}

public enum MilestoneStatus
{
    Planned = 1,
    InProgress = 2,
    Achieved = 3,
    Cancelled = 4,
}

public sealed record MilestonePlan(
    string Name,
    string? Description,
    DateOnly PlannedDate,
    Guid? OwnerEmployeeId,
    Guid? PhaseId,
    MilestoneStatus Status,
    int PercentComplete,
    bool IsBillable);

/// <summary>A checkpoint the project commits to (spec §46).</summary>
public sealed class ProjectMilestone : TenantAggregateRoot
{
    private ProjectMilestone()
    {
        Name = null!;
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateOnly PlannedDate { get; private set; }

    public DateOnly? ActualDate { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? PhaseId { get; private set; }

    public MilestoneStatus Status { get; private set; }

    public int PercentComplete { get; private set; }

    /// <summary>Invoiced on achievement; project billing (M7) picks these up.</summary>
    public bool IsBillable { get; private set; }

    public static ProjectMilestone Create(Guid projectId, MilestonePlan plan)
    {
        var milestone = new ProjectMilestone { ProjectId = Guard.NotEmpty(projectId) };
        milestone.Update(plan);
        return milestone;
    }

    public void Update(MilestonePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.PercentComplete is < 0 or > 100 || !Enum.IsDefined(plan.Status))
        {
            throw new DomainRuleException("PROJECT_MILESTONE_VALUE", "Percent complete is 0-100.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(plan.Name).Trim(), 200);
        Description = Project.Clean(plan.Description, 2000);
        PlannedDate = plan.PlannedDate;
        OwnerEmployeeId = plan.OwnerEmployeeId;
        PhaseId = plan.PhaseId;
        IsBillable = plan.IsBillable;

        if (plan.Status != MilestoneStatus.Achieved)
        {
            ActualDate = null;
        }

        Status = plan.Status;
        PercentComplete = plan.Status == MilestoneStatus.Achieved ? 100 : plan.PercentComplete;
    }

    public void Achieve(DateOnly on)
    {
        if (Status == MilestoneStatus.Cancelled)
        {
            throw new DomainRuleException("PROJECT_MILESTONE_CANCELLED", "A cancelled milestone cannot be achieved.");
        }

        Status = MilestoneStatus.Achieved;
        ActualDate = on;
        PercentComplete = 100;
    }

    public bool IsOpen => Status is MilestoneStatus.Planned or MilestoneStatus.InProgress;

    public int DaysOverdue(DateOnly today) => IsOpen && PlannedDate < today ? today.DayNumber - PlannedDate.DayNumber : 0;
}

public enum SprintStatus
{
    Planned = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4,
}

/// <summary>A time-boxed iteration (spec §48): planned, run, then reviewed with its velocity recorded.</summary>
public sealed class Sprint : TenantAggregateRoot
{
    private Sprint()
    {
        Name = null!;
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public string? Goal { get; private set; }

    /// <summary>Story points the team expects to manage.</summary>
    public int? CapacityPoints { get; private set; }

    public SprintStatus Status { get; private set; }

    /// <summary>Points in the sprint when it started.</summary>
    public int? CommittedPoints { get; private set; }

    /// <summary>Points finished by the end: the sprint's velocity.</summary>
    public int? CompletedPoints { get; private set; }

    public string? ReviewNotes { get; private set; }

    public string? RetrospectiveNotes { get; private set; }

    /// <summary>The burndown as it stood when the sprint closed, kept because tasks move on afterwards.</summary>
    public string? BurndownJson { get; private set; }

    public DateTime? StartedDate { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public static Sprint Plan(Guid projectId, string name, DateOnly start, DateOnly end, string? goal, int? capacityPoints)
    {
        var sprint = new Sprint { ProjectId = Guard.NotEmpty(projectId), Status = SprintStatus.Planned };
        sprint.Update(name, start, end, goal, capacityPoints, null, null);
        return sprint;
    }

    public void Update(string name, DateOnly start, DateOnly end, string? goal, int? capacityPoints, string? reviewNotes, string? retrospectiveNotes)
    {
        if (end < start || end.DayNumber - start.DayNumber > 90)
        {
            throw new DomainRuleException("PROJECT_SPRINT_DATES", "A sprint ends after it starts and lasts at most 90 days.");
        }

        if (capacityPoints is < 0)
        {
            throw new DomainRuleException("PROJECT_SPRINT_CAPACITY", "Capacity cannot be negative.");
        }

        if (Status is SprintStatus.Completed or SprintStatus.Cancelled && (start != StartDate || end != EndDate))
        {
            throw new DomainRuleException("PROJECT_SPRINT_CLOSED", "A finished sprint's dates are history and cannot move.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        StartDate = start;
        EndDate = end;
        Goal = Project.Clean(goal, 1000);
        CapacityPoints = capacityPoints;
        ReviewNotes = Project.Clean(reviewNotes, 4000);
        RetrospectiveNotes = Project.Clean(retrospectiveNotes, 4000);
    }

    public void Start(int committedPoints, DateTime now)
    {
        if (Status != SprintStatus.Planned)
        {
            throw new DomainRuleException("PROJECT_SPRINT_STATE", $"Only a planned sprint can start; this one is {Status}.");
        }

        Status = SprintStatus.Active;
        CommittedPoints = committedPoints;
        StartedDate = now;
    }

    public void Complete(int completedPoints, string burndownJson, DateTime now)
    {
        if (Status != SprintStatus.Active)
        {
            throw new DomainRuleException("PROJECT_SPRINT_STATE", $"Only an active sprint can be completed; this one is {Status}.");
        }

        Status = SprintStatus.Completed;
        CompletedPoints = completedPoints;
        BurndownJson = burndownJson;
        CompletedDate = now;
    }

    public void Cancel()
    {
        if (Status is SprintStatus.Completed or SprintStatus.Cancelled)
        {
            throw new DomainRuleException("PROJECT_SPRINT_STATE", $"This sprint is already {Status}.");
        }

        Status = SprintStatus.Cancelled;
    }

    public bool IsOpen => Status is SprintStatus.Planned or SprintStatus.Active;
}

public enum RequirementType
{
    Business = 1,
    Functional = 2,
    NonFunctional = 3,
    Technical = 4,
    Compliance = 5,
}

/// <summary>MoSCoW: must, should, could, won't (this time).</summary>
public enum RequirementPriority
{
    Must = 1,
    Should = 2,
    Could = 3,
    Wont = 4,
}

public enum RequirementStatus
{
    Draft = 1,
    Approved = 2,
    InProgress = 3,
    Implemented = 4,
    Verified = 5,
    Rejected = 6,
    Deferred = 7,
}

public sealed record RequirementDetails(
    string Title,
    string? Description,
    RequirementType Type,
    RequirementPriority Priority,
    string? Source,
    Guid? OwnerEmployeeId,
    RequirementStatus Status,
    string? AcceptanceCriteria);

/// <summary>What the client needs (spec §49), traced through the tasks that deliver it.</summary>
public sealed class Requirement : TenantAggregateRoot
{
    private Requirement()
    {
        Code = null!;
        Title = null!;
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public RequirementType Type { get; private set; }

    public RequirementPriority Priority { get; private set; }

    /// <summary>Where it came from: a workshop, the RFP, a regulation.</summary>
    public string? Source { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public RequirementStatus Status { get; private set; }

    public string? AcceptanceCriteria { get; private set; }

    public static Requirement Create(Guid projectId, int number, RequirementDetails details)
    {
        var requirement = new Requirement { ProjectId = Guard.NotEmpty(projectId), Number = number, Code = $"REQ-{number}" };
        requirement.Update(details);
        return requirement;
    }

    public void Update(RequirementDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!Enum.IsDefined(details.Type) || !Enum.IsDefined(details.Priority) || !Enum.IsDefined(details.Status))
        {
            throw new DomainRuleException("PROJECT_REQUIREMENT_VALUE", "Unknown requirement type, priority or status.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        Description = Project.Clean(details.Description, 8000);
        Type = details.Type;
        Priority = details.Priority;
        Source = Project.Clean(details.Source, 200);
        OwnerEmployeeId = details.OwnerEmployeeId;
        Status = details.Status;
        AcceptanceCriteria = Project.Clean(details.AcceptanceCriteria, 4000);
    }
}
