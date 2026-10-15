using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Leave.Contracts;

/// <summary>A day, or half a day, someone is on approved leave.</summary>
public sealed record LeaveDay(Guid EmployeeId, DateOnly Date, string LeaveTypeCode, string LeaveTypeName, bool IsHalfDay, bool IsPaid);

/// <summary>A public holiday on someone's calendar. Optional holidays are listed but are working days.</summary>
public sealed record HolidayEntry(DateOnly Date, string Name, bool IsOptional);

/// <summary>Leave and holidays by day, for Attendance and Payroll to judge a day by (ADR-12).</summary>
public interface ILeaveCalendar
{
    Task<IReadOnlyList<LeaveDay>> GetApprovedLeaveAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>Each employee's holidays, from the calendar for their location (or the default one).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>>> GetHolidaysAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);
}

/// <summary>Leave was approved: those days are no longer working days for the employee.</summary>
[MessageType("gggs.leave.request-approved")]
public sealed record LeaveApprovedIntegrationEvent(Guid RequestId, Guid EmployeeId, DateOnly StartDate, DateOnly EndDate, decimal Days, string LeaveTypeCode) : IntegrationEvent;

/// <summary>Approved leave was cancelled: the days are working days again.</summary>
[MessageType("gggs.leave.request-cancelled")]
public sealed record LeaveCancelledIntegrationEvent(Guid RequestId, Guid EmployeeId, DateOnly StartDate, DateOnly EndDate) : IntegrationEvent;
