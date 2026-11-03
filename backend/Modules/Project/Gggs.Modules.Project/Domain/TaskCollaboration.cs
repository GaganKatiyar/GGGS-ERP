using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

/// <summary>Someone saying something on a task. Who it names are worked out before it gets here.</summary>
public sealed class TaskComment : TenantAggregateRoot
{
    private TaskComment()
    {
        Body = null!;
    }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid AuthorEmployeeId { get; private set; }

    public string Body { get; private set; }

    public DateTime PostedAt { get; private set; }

    public DateTime? EditedAt { get; private set; }

    public static TaskComment Post(Guid projectId, Guid taskId, Guid authorEmployeeId, string body, DateTime now)
        => new()
        {
            ProjectId = Guard.NotEmpty(projectId),
            TaskId = Guard.NotEmpty(taskId),
            AuthorEmployeeId = Guard.NotEmpty(authorEmployeeId),
            Body = Clean(body),
            PostedAt = now,
        };

    public void Edit(string body, DateTime now)
    {
        Body = Clean(body);
        EditedAt = now;
    }

    private static string Clean(string body) => Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000);
}

/// <summary>Someone who wants to hear about a task they neither own nor reported.</summary>
public sealed class TaskWatcher : TenantAggregateRoot
{
    private TaskWatcher()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public static TaskWatcher For(Guid projectId, Guid taskId, Guid employeeId)
        => new() { ProjectId = Guard.NotEmpty(projectId), TaskId = Guard.NotEmpty(taskId), EmployeeId = Guard.NotEmpty(employeeId) };
}

/// <summary>One change to a task. The values are kept as they read then, so the history survives a rename or a departure.</summary>
public sealed class TaskActivity : TenantAggregateRoot
{
    private TaskActivity()
    {
        Field = null!;
    }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid? ActorEmployeeId { get; private set; }

    public DateTime At { get; private set; }

    public string Field { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public static TaskActivity Record(ProjectTask task, Guid? actorEmployeeId, DateTime at, string field, string? oldValue, string? newValue)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskActivity
        {
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            ActorEmployeeId = actorEmployeeId,
            At = at,
            Field = field,
            OldValue = Short(oldValue),
            NewValue = Short(newValue),
        };
    }

    private static string? Short(string? value) => value is { Length: > 300 } ? value[..300] : value;
}

/// <summary>What a task looked like, to tell what a change changed.</summary>
public sealed record TaskState(
    TaskType Type,
    string Title,
    Guid? AssigneeEmployeeId,
    WorkPriority Priority,
    Guid ColumnId,
    Guid? SprintId,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    int? StoryPoints)
{
    public static TaskState Of(ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskState(task.Type, task.Title, task.AssigneeEmployeeId, task.Priority, task.ColumnId, task.SprintId, task.StartDate, task.DueDate, task.EstimatedHours, task.StoryPoints);
    }
}

/// <summary>The fields a task's history tracks.</summary>
public static class TaskFields
{
    public const string Created = "created";
    public const string Imported = "imported";
    public const string Type = "type";
    public const string Title = "title";
    public const string Assignee = "assignee";
    public const string Priority = "priority";
    public const string Column = "column";
    public const string Sprint = "sprint";
    public const string StartDate = "startDate";
    public const string DueDate = "dueDate";
    public const string Estimate = "estimate";
    public const string Points = "points";

    /// <summary>Which tracked fields differ, so a save that changed nothing records nothing.</summary>
    public static IReadOnlyList<string> Changed(TaskState before, TaskState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        List<string> changed = [];
        void Check(bool same, string field)
        {
            if (!same)
            {
                changed.Add(field);
            }
        }

        Check(before.Type == after.Type, Type);
        Check(before.Title == after.Title, Title);
        Check(before.AssigneeEmployeeId == after.AssigneeEmployeeId, Assignee);
        Check(before.Priority == after.Priority, Priority);
        Check(before.ColumnId == after.ColumnId, Column);
        Check(before.SprintId == after.SprintId, Sprint);
        Check(before.StartDate == after.StartDate, StartDate);
        Check(before.DueDate == after.DueDate, DueDate);
        Check(before.EstimatedHours == after.EstimatedHours, Estimate);
        Check(before.StoryPoints == after.StoryPoints, Points);
        return changed;
    }
}

/// <summary>What a saved view filters by.</summary>
public sealed record TaskViewFilters(
    string? Search = null,
    TaskType? Type = null,
    Guid? AssigneeEmployeeId = null,
    bool? Unassigned = null,
    WorkPriority? Priority = null,
    Guid? SprintId = null,
    bool? Backlog = null,
    ColumnCategory? Category = null,
    bool? Overdue = null,
    bool? IncludeDone = null);

/// <summary>A filter worth keeping: someone's own, or, with no owner, shared with everyone on the project.</summary>
public sealed class TaskSavedView : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TaskSavedView()
    {
        Name = null!;
        FiltersJson = "{}";
    }

    public Guid ProjectId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string Name { get; private set; }

    public string FiltersJson { get; private set; }

    public bool IsShared => OwnerEmployeeId is null;

    public TaskViewFilters Filters => JsonSerializer.Deserialize<TaskViewFilters>(FiltersJson, Json) ?? new TaskViewFilters();

    public static TaskSavedView Save(Guid projectId, Guid? ownerEmployeeId, string name, TaskViewFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return new TaskSavedView
        {
            ProjectId = Guard.NotEmpty(projectId),
            OwnerEmployeeId = ownerEmployeeId,
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            FiltersJson = JsonSerializer.Serialize(filters, Json),
        };
    }
}

/// <summary>Working out how much is on someone's plate, week by week.</summary>
public static class Workload
{
    public const decimal HoursPerDay = 8;

    public sealed record Assignment(decimal AllocationPercent, DateOnly StartDate, DateOnly? EndDate);

    public sealed record OpenTask(DateOnly? StartDate, DateOnly? DueDate, decimal? EstimatedHours, decimal? ActualHours);

    public sealed record Week(DateOnly WeekStart, decimal PlannedHours, decimal CapacityHours);

    public sealed record Person(int OpenTasks, int Overdue, int Unestimated, decimal RemainingHours, decimal UnscheduledHours, IReadOnlyList<Week> Weeks);

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// A person's next weeks. What is left on each task spreads evenly over the working days from now (or its
    /// start) to its due date; late work lands on the first working day, and work with no due date has no week to go in.
    /// </summary>
    public static Person For(IReadOnlyList<Assignment> assignments, IReadOnlyList<OpenTask> tasks, DateOnly today, int weeks)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(tasks);
        DateOnly first = MondayOf(today);
        var planned = new decimal[weeks];
        decimal remainingTotal = 0;
        decimal unscheduled = 0;
        int overdue = 0;
        int unestimated = 0;

        foreach (OpenTask task in tasks)
        {
            if (task.DueDate is { } late && late < today)
            {
                overdue++;
            }

            if (task.EstimatedHours is not { } estimate)
            {
                unestimated++;
                continue;
            }

            decimal remaining = Math.Max(0, estimate - (task.ActualHours ?? 0));
            remainingTotal += remaining;
            if (remaining == 0)
            {
                continue;
            }

            if (task.DueDate is not { } due)
            {
                unscheduled += remaining;
                continue;
            }

            DateOnly from = task.StartDate is { } start && start > today ? start : today;
            List<DateOnly> days = [];
            for (DateOnly day = from; day <= due; day = day.AddDays(1))
            {
                if (IsWorkingDay(day))
                {
                    days.Add(day);
                }
            }

            if (days.Count == 0)
            {
                DateOnly next = today;
                while (!IsWorkingDay(next))
                {
                    next = next.AddDays(1);
                }

                days.Add(next);
            }

            decimal share = remaining / days.Count;
            foreach (DateOnly day in days)
            {
                int index = (day.DayNumber - first.DayNumber) / 7;
                if (index >= 0 && index < weeks)
                {
                    planned[index] += share;
                }
            }
        }

        List<Week> rows = [];
        for (int index = 0; index < weeks; index++)
        {
            DateOnly weekStart = first.AddDays(index * 7);
            rows.Add(new Week(weekStart, Math.Round(planned[index], 1), Math.Round(Capacity(assignments, weekStart), 1)));
        }

        return new Person(tasks.Count, overdue, unestimated, Math.Round(remainingTotal, 1), Math.Round(unscheduled, 1), rows);
    }

    /// <summary>The hours this project has of them in a week, from their allocation on each working day.</summary>
    private static decimal Capacity(IReadOnlyList<Assignment> assignments, DateOnly weekStart)
    {
        decimal hours = 0;
        for (int offset = 0; offset < 5; offset++)
        {
            DateOnly day = weekStart.AddDays(offset);
            decimal percent = assignments.Where(row => row.StartDate <= day && (row.EndDate is null || row.EndDate >= day)).Sum(row => row.AllocationPercent);
            hours += HoursPerDay * percent / 100;
        }

        return hours;
    }
}
