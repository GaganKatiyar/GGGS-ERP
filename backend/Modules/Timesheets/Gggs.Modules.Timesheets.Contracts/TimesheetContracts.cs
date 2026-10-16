using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Timesheets.Contracts;

/// <summary>
/// How an hour is classified (spec §73). Billable hours are what utilization measures; the rest say
/// where the other time went.
/// </summary>
public enum HourCategory
{
    Billable = 1,
    NonBillable = 2,
    Internal = 3,
    Training = 4,
    Bench = 5,
    Leave = 6,
}

/// <summary>Hours one person booked on one day under one classification, for one project or none.</summary>
public sealed record LoggedHours(Guid EmployeeId, Guid? ProjectId, Guid? TaskId, DateOnly Date, HourCategory Category, decimal Hours, bool Approved);

/// <summary>Booked hours for utilization, billing and payroll (ADR-12).</summary>
public interface ITimesheetLedger
{
    /// <summary>Hours in the window from submitted and approved timesheets; <paramref name="employeeIds"/> null means everyone.</summary>
    Task<IReadOnlyList<LoggedHours>> HoursAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, bool approvedOnly, CancellationToken cancellationToken = default);

    /// <summary>Hours booked to the given projects in the window (all projects when null), for project costing and revenue.</summary>
    Task<IReadOnlyList<LoggedHours>> ProjectHoursAsync(IEnumerable<Guid>? projectIds, DateOnly firstDay, DateOnly lastDay, bool approvedOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved hours beyond the standard day, per person, in the window (spec §94): hours over the
    /// standard on a working day, and every hour on a weekly off or holiday. Payroll prices them.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> OvertimeAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);
}

public sealed record TaskHours(Guid TaskId, decimal Hours);

/// <summary>A timesheet was approved: its hours count. Projects add them to their tasks' actual hours.</summary>
[MessageType("gggs.timesheets.approved")]
public sealed record TimesheetApprovedIntegrationEvent(Guid TimesheetId, Guid EmployeeId, DateOnly WeekStart, IReadOnlyList<TaskHours> TaskHours) : IntegrationEvent;

/// <summary>An approved timesheet was reopened for correction: its hours no longer count until approved again.</summary>
[MessageType("gggs.timesheets.reopened")]
public sealed record TimesheetReopenedIntegrationEvent(Guid TimesheetId, Guid EmployeeId, DateOnly WeekStart, IReadOnlyList<TaskHours> TaskHours) : IntegrationEvent;
