using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Timesheets.Contracts;

namespace Gggs.Modules.Timesheets.Domain;

public enum TimesheetStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>
/// One person's week (spec §70, §124): filled in, submitted, approved - and then fixed, unless it is
/// reopened for a correction before its period is locked.
/// </summary>
public sealed class Timesheet : TenantAggregateRoot
{
    private Timesheet()
    {
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>The Monday the week starts on.</summary>
    public DateOnly WeekStart { get; private set; }

    public TimesheetStatus Status { get; private set; }

    public decimal TotalHours { get; private set; }

    public decimal BillableHours { get; private set; }

    public DateTime? SubmittedDate { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public bool IsLate { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>How often it was reopened after approval (spec §124 TimesheetCorrection).</summary>
    public int CorrectionCount { get; private set; }

    public string? LastCorrectionReason { get; private set; }

    public DateOnly WeekEnd => WeekStart.AddDays(6);

    public static Timesheet Open(Guid employeeId, DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new DomainRuleException("TIMESHEET_WEEK", "A timesheet week starts on a Monday.");
        }

        return new Timesheet { EmployeeId = Guard.NotEmpty(employeeId), WeekStart = weekStart, Status = TimesheetStatus.Draft };
    }

    public static DateOnly WeekOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Throws unless the week can still be edited: a draft (or rejected) week outside any locked period.</summary>
    public void EnsureEditable(DateOnly? lockedThrough)
    {
        if (Status is TimesheetStatus.Submitted or TimesheetStatus.Approved)
        {
            throw new DomainRuleException("TIMESHEET_FROZEN", $"This week is {Status.ToString().ToLowerInvariant()}; withdraw it or ask for it to be reopened first.");
        }

        if (lockedThrough is { } locked && WeekStart <= locked)
        {
            throw new DomainRuleException("TIMESHEET_LOCKED", $"Timesheets up to {locked:d MMM yyyy} are locked.");
        }
    }

    public void Recalculate(IEnumerable<TimesheetEntry> entries, string? comment)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<TimesheetEntry> list = [.. entries];
        TotalHours = list.Sum(entry => entry.Hours);
        BillableHours = list.Where(entry => entry.Category == HourCategory.Billable).Sum(entry => entry.Hours);
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 1000);
    }

    public void Submit(DateTime now, bool isLate)
    {
        if (Status is not (TimesheetStatus.Draft or TimesheetStatus.Rejected))
        {
            throw new DomainRuleException("TIMESHEET_STATE", $"A {Status.ToString().ToLowerInvariant()} timesheet cannot be submitted.");
        }

        if (TotalHours <= 0)
        {
            throw new DomainRuleException("TIMESHEET_EMPTY", "Book some hours before submitting the week.");
        }

        Status = TimesheetStatus.Submitted;
        SubmittedDate = now;
        IsLate = isLate;
        DecidedDate = null;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Approve(DateTime now)
    {
        if (Status != TimesheetStatus.Submitted)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only a submitted timesheet can be approved.");
        }

        Status = TimesheetStatus.Approved;
        DecidedDate = now;
    }

    public void Reject(DateTime now)
    {
        if (Status == TimesheetStatus.Submitted)
        {
            Status = TimesheetStatus.Rejected;
            DecidedDate = now;
            WorkflowInstanceId = null;
        }
    }

    /// <summary>Withdrawn or sent back: a draft again.</summary>
    public void ReturnToDraft()
    {
        if (Status == TimesheetStatus.Submitted)
        {
            Status = TimesheetStatus.Draft;
            WorkflowInstanceId = null;
        }
    }

    /// <summary>An approved week reopened for a correction (spec §71). Locked periods stay shut.</summary>
    public void Reopen(string reason, DateOnly? lockedThrough)
    {
        if (Status != TimesheetStatus.Approved)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only an approved timesheet is reopened; a draft can simply be edited.");
        }

        if (lockedThrough is { } locked && WeekStart <= locked)
        {
            throw new DomainRuleException("TIMESHEET_LOCKED", $"Timesheets up to {locked:d MMM yyyy} are locked.");
        }

        Status = TimesheetStatus.Draft;
        WorkflowInstanceId = null;
        DecidedDate = null;
        CorrectionCount++;
        LastCorrectionReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500);
    }
}

/// <summary>Hours on one day (spec §70 TimesheetEntry), against a project and task or a non-project category.</summary>
public sealed class TimesheetEntry : TenantAggregateRoot
{
    private TimesheetEntry()
    {
    }

    public Guid TimesheetId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    public HourCategory Category { get; private set; }

    public decimal Hours { get; private set; }

    /// <summary>Development, testing, meetings, support…</summary>
    public string? Activity { get; private set; }

    public string? Description { get; private set; }

    public static TimesheetEntry Book(Timesheet timesheet, DateOnly date, Guid? projectId, Guid? taskId, HourCategory category, decimal hours, string? activity, string? description)
    {
        ArgumentNullException.ThrowIfNull(timesheet);

        if (date < timesheet.WeekStart || date > timesheet.WeekEnd)
        {
            throw new DomainRuleException("TIMESHEET_DATE", "Every entry falls within the timesheet's week.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainRuleException("TIMESHEET_CATEGORY", "Unknown hour category.");
        }

        if (hours <= 0 || hours > 24 || hours * 4 != decimal.Truncate(hours * 4))
        {
            throw new DomainRuleException("TIMESHEET_HOURS", "Hours are booked in quarter hours, up to 24 a day.");
        }

        if (category is HourCategory.Billable or HourCategory.NonBillable && projectId is null)
        {
            throw new DomainRuleException("TIMESHEET_PROJECT_REQUIRED", "Billable and non-billable project hours need the project.");
        }

        if (taskId is not null && projectId is null)
        {
            throw new DomainRuleException("TIMESHEET_PROJECT_REQUIRED", "A task is booked under its project.");
        }

        return new TimesheetEntry
        {
            TimesheetId = timesheet.Id,
            EmployeeId = timesheet.EmployeeId,
            Date = date,
            ProjectId = projectId,
            TaskId = taskId,
            Category = category,
            Hours = hours,
            Activity = string.IsNullOrWhiteSpace(activity) ? null : Guard.MaxLength(activity.Trim(), 50),
            Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500),
        };
    }
}

/// <summary>
/// The tenant's timesheet rules (spec §71) and the locked period. One row per tenant, created with the
/// defaults the first time anyone asks.
/// </summary>
public sealed class TimesheetSettings : TenantAggregateRoot
{
    private TimesheetSettings()
    {
    }

    public decimal StandardHoursPerDay { get; private set; } = 8;

    /// <summary>Working days booked below this get a warning.</summary>
    public decimal MinHoursPerDay { get; private set; } = 8;

    /// <summary>No day may hold more.</summary>
    public decimal MaxHoursPerDay { get; private set; } = 12;

    public decimal MinWeeklyHours { get; private set; } = 40;

    /// <summary>When set, a week below the minimum cannot be submitted; otherwise it is only warned about.</summary>
    public bool EnforceWeeklyMinimum { get; private set; }

    public bool AllowWeekendEntries { get; private set; } = true;

    public bool AllowHolidayEntries { get; private set; } = true;

    /// <summary>Days after the week ends that a submission still counts as on time.</summary>
    public int SubmissionDeadlineDays { get; private set; } = 2;

    /// <summary>Nothing on or before this date changes (spec §124 TimesheetLock).</summary>
    public DateOnly? LockedThrough { get; private set; }

    public static TimesheetSettings Defaults() => new();

    public void Update(TimesheetRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.StandardHoursPerDay is <= 0 or > 24 || rules.MaxHoursPerDay is <= 0 or > 24 || rules.MinHoursPerDay < 0
            || rules.MinHoursPerDay > rules.MaxHoursPerDay || rules.MinWeeklyHours is < 0 or > 168 || rules.SubmissionDeadlineDays is < 0 or > 60)
        {
            throw new DomainRuleException("TIMESHEET_RULES", "The hour limits do not add up: a day holds 0-24 hours, the minimum at or below the maximum.");
        }

        StandardHoursPerDay = rules.StandardHoursPerDay;
        MinHoursPerDay = rules.MinHoursPerDay;
        MaxHoursPerDay = rules.MaxHoursPerDay;
        MinWeeklyHours = rules.MinWeeklyHours;
        EnforceWeeklyMinimum = rules.EnforceWeeklyMinimum;
        AllowWeekendEntries = rules.AllowWeekendEntries;
        AllowHolidayEntries = rules.AllowHolidayEntries;
        SubmissionDeadlineDays = rules.SubmissionDeadlineDays;
    }

    /// <summary>Locks every week ending on or before <paramref name="through"/>. A lock only moves forward.</summary>
    public void LockThrough(DateOnly through)
    {
        if (LockedThrough is { } current && through < current)
        {
            throw new DomainRuleException("TIMESHEET_LOCK_BACKWARDS", $"Timesheets are already locked through {current:d MMM yyyy}; a lock cannot be undone.");
        }

        LockedThrough = through;
    }

    public TimesheetRules ToRules() => new(
        StandardHoursPerDay, MinHoursPerDay, MaxHoursPerDay, MinWeeklyHours, EnforceWeeklyMinimum, AllowWeekendEntries, AllowHolidayEntries, SubmissionDeadlineDays, LockedThrough);
}

public sealed record TimesheetRules(
    decimal StandardHoursPerDay,
    decimal MinHoursPerDay,
    decimal MaxHoursPerDay,
    decimal MinWeeklyHours,
    bool EnforceWeeklyMinimum,
    bool AllowWeekendEntries,
    bool AllowHolidayEntries,
    int SubmissionDeadlineDays,
    DateOnly? LockedThrough)
{
    public static TimesheetRules Default { get; } = TimesheetSettings.Defaults().ToRules();
}

/// <summary>What one day looks like for the rules: working or not, a holiday, leave.</summary>
public sealed record DayFacts(DateOnly Date, bool IsWorkingDay, string? Holiday, decimal LeaveFraction);

public enum IssueLevel
{
    Info = 1,
    Warning = 2,
    Error = 3,
}

public sealed record TimesheetIssue(IssueLevel Level, string Code, string Message, DateOnly? Date);

/// <summary>The timesheet rules (spec §71) applied to a week: errors stop a submission, warnings do not.</summary>
public static class TimesheetChecks
{
    public static IReadOnlyList<TimesheetIssue> Check(
        TimesheetRules rules,
        IReadOnlyList<DayFacts> days,
        IEnumerable<(DateOnly Date, decimal Hours)> entries)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<DateOnly, decimal> perDay = entries.GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours));
        List<TimesheetIssue> issues = [];
        decimal overtime = 0;

        foreach (DayFacts day in days)
        {
            decimal hours = perDay.GetValueOrDefault(day.Date);

            if (hours > rules.MaxHoursPerDay)
            {
                issues.Add(new TimesheetIssue(IssueLevel.Error, "TIMESHEET_DAY_MAX", $"{day.Date:ddd d MMM} has {hours:0.##} h; the most a day may hold is {rules.MaxHoursPerDay:0.##}.", day.Date));
            }

            if (hours > 0 && !day.IsWorkingDay && day.Holiday is null && !rules.AllowWeekendEntries)
            {
                issues.Add(new TimesheetIssue(IssueLevel.Error, "TIMESHEET_WEEKEND", $"{day.Date:ddd d MMM} is not a working day, and weekend hours are not allowed.", day.Date));
            }

            if (hours > 0 && day.Holiday is not null && !rules.AllowHolidayEntries)
            {
                issues.Add(new TimesheetIssue(IssueLevel.Error, "TIMESHEET_HOLIDAY", $"{day.Date:ddd d MMM} is {day.Holiday}, and holiday hours are not allowed.", day.Date));
            }

            decimal expected = day.IsWorkingDay && day.Holiday is null ? rules.MinHoursPerDay * (1 - day.LeaveFraction) : 0;
            if (expected > 0 && hours < expected)
            {
                issues.Add(new TimesheetIssue(IssueLevel.Warning, "TIMESHEET_DAY_SHORT", $"{day.Date:ddd d MMM} has {hours:0.##} h of an expected {expected:0.##}.", day.Date));
            }

            overtime += Math.Max(0, hours - (day.IsWorkingDay && day.Holiday is null ? rules.StandardHoursPerDay : 0));
        }

        decimal total = perDay.Values.Sum();
        decimal leaveHours = days.Sum(day => day.LeaveFraction * rules.StandardHoursPerDay);
        decimal holidayHours = days.Count(day => day.IsWorkingDay && day.Holiday is not null) * rules.StandardHoursPerDay;
        decimal weeklyMinimum = Math.Max(0, rules.MinWeeklyHours - leaveHours - holidayHours);
        if (total < weeklyMinimum)
        {
            issues.Add(new TimesheetIssue(
                rules.EnforceWeeklyMinimum ? IssueLevel.Error : IssueLevel.Warning,
                "TIMESHEET_WEEK_SHORT",
                $"The week has {total:0.##} h of an expected {weeklyMinimum:0.##}.",
                null));
        }

        if (overtime > 0)
        {
            issues.Add(new TimesheetIssue(IssueLevel.Info, "TIMESHEET_OVERTIME", $"{overtime:0.##} h beyond the standard day.", null));
        }

        return issues;
    }

    public static decimal Overtime(TimesheetRules rules, IReadOnlyList<DayFacts> days, IEnumerable<(DateOnly Date, decimal Hours)> entries)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(days);
        Dictionary<DateOnly, decimal> perDay = entries.GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours));
        return days.Sum(day => Math.Max(0, perDay.GetValueOrDefault(day.Date) - (day.IsWorkingDay && day.Holiday is null ? rules.StandardHoursPerDay : 0)));
    }

    /// <summary>Submitted after the deadline: the week's end plus the grace days.</summary>
    public static bool IsLate(DateOnly weekEnd, DateOnly submittedOn, int deadlineDays) => submittedOn > weekEnd.AddDays(deadlineDays);
}
