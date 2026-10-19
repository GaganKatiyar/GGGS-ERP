using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Performance.Domain;

public enum GoalLevel
{
    Company = 1,
    Department = 2,
    Team = 3,
    Individual = 4,
}

public enum GoalType
{
    /// <summary>An objective measured by key results.</summary>
    Okr = 1,

    /// <summary>A standing indicator with a target, measured the same way.</summary>
    Kpi = 2,
}

public enum GoalStatus
{
    Draft = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4,
}

public enum Confidence
{
    OnTrack = 1,
    AtRisk = 2,
    OffTrack = 3,
}

public enum MetricType
{
    Number = 1,
    Percent = 2,
    Currency = 3,

    /// <summary>Done or not: 0 or 1.</summary>
    Milestone = 4,
}

public sealed record GoalDetails(
    string Title,
    string? Description,
    GoalType Type,
    GoalLevel Level,
    Guid? OwnerEmployeeId,
    Guid? DepartmentId,
    Guid? ParentGoalId,
    Guid? CycleId,
    DateOnly StartDate,
    DateOnly DueDate,
    decimal Weight);

/// <summary>
/// A goal (spec: goals, OKR and KPI): company, department, team or individual, cascading from a parent,
/// measured by its key results (or a manual progress when it has none), with a weight that counts
/// towards the owner's review.
/// </summary>
public sealed class Goal : TenantAggregateRoot
{
    private Goal()
    {
        Title = null!;
    }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public GoalType Type { get; private set; }

    public GoalLevel Level { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? ParentGoalId { get; private set; }

    public Guid? CycleId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public decimal Weight { get; private set; }

    public GoalStatus Status { get; private set; }

    /// <summary>0-100.</summary>
    public decimal Progress { get; private set; }

    public Confidence? Confidence { get; private set; }

    public DateOnly? LastCheckInOn { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public static Goal Create(GoalDetails details, bool activate)
    {
        var goal = new Goal { Status = activate ? GoalStatus.Active : GoalStatus.Draft };
        goal.Apply(details);
        return goal;
    }

    public void Update(GoalDetails details)
    {
        EnsureOpen();
        Apply(details);
    }

    public void Activate()
    {
        if (Status != GoalStatus.Draft)
        {
            throw new DomainRuleException("GOAL_STATE", "Only a draft goal can be activated.");
        }

        Status = GoalStatus.Active;
    }

    public void Complete(DateOnly today)
    {
        EnsureOpen();
        Status = GoalStatus.Completed;
        CompletedOn = today;
    }

    public void Cancel()
    {
        EnsureOpen();
        Status = GoalStatus.Cancelled;
    }

    public void Reopen()
    {
        if (Status is not (GoalStatus.Completed or GoalStatus.Cancelled))
        {
            throw new DomainRuleException("GOAL_STATE", "The goal is already open.");
        }

        Status = GoalStatus.Active;
        CompletedOn = null;
    }

    public void Record(decimal progress, Confidence? confidence, DateOnly day)
    {
        if (Status == GoalStatus.Cancelled)
        {
            throw new DomainRuleException("GOAL_STATE", "The goal was cancelled.");
        }

        if (Status == GoalStatus.Draft)
        {
            Status = GoalStatus.Active;
        }

        Progress = Math.Round(Math.Clamp(progress, 0, 100), 1);
        Confidence = confidence ?? Confidence;
        LastCheckInOn = day;
    }

    public void EnsureOpen()
    {
        if (Status is GoalStatus.Completed or GoalStatus.Cancelled)
        {
            throw new DomainRuleException("GOAL_STATE", $"The goal is {Status.ToString().ToLowerInvariant()}; reopen it first.");
        }
    }

    private void Apply(GoalDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Level == GoalLevel.Individual && details.OwnerEmployeeId is null)
        {
            throw new DomainRuleException("GOAL_OWNER", "An individual goal needs an owner.");
        }

        if (details.Level is GoalLevel.Department or GoalLevel.Team && details.DepartmentId is null)
        {
            throw new DomainRuleException("GOAL_DEPARTMENT", "A department or team goal needs a department.");
        }

        if (details.DueDate < details.StartDate)
        {
            throw new DomainRuleException("GOAL_DATES", "The goal ends before it starts.");
        }

        if (details.Weight is < 0 or > 100)
        {
            throw new DomainRuleException("GOAL_WEIGHT", "A goal's weight is between 0 and 100.");
        }

        if (details.ParentGoalId is { } parent && parent == Id)
        {
            throw new DomainRuleException("GOAL_PARENT", "A goal cannot be its own parent.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 4000);
        Type = details.Type;
        Level = details.Level;
        OwnerEmployeeId = details.OwnerEmployeeId;
        DepartmentId = details.DepartmentId;
        ParentGoalId = details.ParentGoalId;
        CycleId = details.CycleId;
        StartDate = details.StartDate;
        DueDate = details.DueDate;
        Weight = details.Weight;
    }
}

public sealed record KeyResultDetails(string Title, MetricType MetricType, decimal StartValue, decimal TargetValue, string? Unit, decimal Weight);

/// <summary>A measurable result of a goal: from a start value to a target, with the value reached so far.</summary>
public sealed class KeyResult : TenantAggregateRoot
{
    private KeyResult()
    {
        Title = null!;
    }

    public Guid GoalId { get; private set; }

    public string Title { get; private set; }

    public MetricType MetricType { get; private set; }

    public decimal StartValue { get; private set; }

    public decimal TargetValue { get; private set; }

    public decimal CurrentValue { get; private set; }

    public string? Unit { get; private set; }

    public decimal Weight { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>0-100: how far from the start value to the target the current value is.</summary>
    public decimal Progress => Measure(MetricType, StartValue, TargetValue, CurrentValue);

    public static KeyResult Add(Guid goalId, KeyResultDetails details, int sortOrder)
    {
        var result = new KeyResult { GoalId = Guard.NotEmpty(goalId), SortOrder = sortOrder };
        result.Update(details);
        result.CurrentValue = result.StartValue;
        return result;
    }

    public void Update(KeyResultDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        bool milestone = details.MetricType == MetricType.Milestone;
        if (!milestone && details.StartValue == details.TargetValue)
        {
            throw new DomainRuleException("KEY_RESULT_TARGET", "The target must differ from the start value.");
        }

        if (details.Weight is <= 0 or > 100)
        {
            throw new DomainRuleException("KEY_RESULT_WEIGHT", "A key result's weight is between 1 and 100.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 300);
        MetricType = details.MetricType;
        StartValue = milestone ? 0 : details.StartValue;
        TargetValue = milestone ? 1 : details.TargetValue;
        Unit = string.IsNullOrWhiteSpace(details.Unit) ? null : Guard.MaxLength(details.Unit.Trim(), 20);
        Weight = details.Weight;
    }

    public void Record(decimal value) => CurrentValue = MetricType == MetricType.Milestone ? (value >= 1 ? 1 : 0) : value;

    /// <summary>Progress towards a target. Works for decreasing targets (cost down from 100 to 60) as well.</summary>
    public static decimal Measure(MetricType type, decimal start, decimal target, decimal current)
    {
        if (type == MetricType.Milestone)
        {
            return current >= 1 ? 100 : 0;
        }

        if (target == start)
        {
            return current == target ? 100 : 0;
        }

        return Math.Round(Math.Clamp((current - start) / (target - start) * 100, 0, 100), 1);
    }

    /// <summary>A goal's progress from its key results: their progress weighted by their weights.</summary>
    public static decimal Rollup(IReadOnlyCollection<KeyResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        decimal weights = results.Sum(result => result.Weight);
        return weights == 0 ? 0 : Math.Round(results.Sum(result => result.Progress * result.Weight) / weights, 1);
    }
}

/// <summary>A key result's value at a check-in.</summary>
public sealed record KeyResultValue(Guid KeyResultId, decimal Value);

/// <summary>A progress update on a goal: where it stands, how confident the owner is, and what happened.</summary>
public sealed class GoalCheckIn : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private GoalCheckIn()
    {
        ValuesJson = "[]";
    }

    public Guid GoalId { get; private set; }

    public Guid? ByEmployeeId { get; private set; }

    public DateOnly CheckInOn { get; private set; }

    public decimal Progress { get; private set; }

    public Confidence Confidence { get; private set; }

    public string? Note { get; private set; }

    public string ValuesJson { get; private set; }

    public IReadOnlyList<KeyResultValue> Values => JsonSerializer.Deserialize<List<KeyResultValue>>(ValuesJson, Json) ?? [];

    public static GoalCheckIn Record(Guid goalId, Guid? byEmployeeId, DateOnly day, decimal progress, Confidence confidence, string? note, IReadOnlyList<KeyResultValue> values) => new()
    {
        GoalId = Guard.NotEmpty(goalId),
        ByEmployeeId = byEmployeeId,
        CheckInOn = day,
        Progress = Math.Round(Math.Clamp(progress, 0, 100), 1),
        Confidence = confidence,
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 2000),
        ValuesJson = JsonSerializer.Serialize(values ?? [], Json),
    };
}
