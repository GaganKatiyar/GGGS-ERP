using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum RosterSource
{
    Pattern = 1,
    Manual = 2,
    Swap = 3,
}

/// <summary>
/// One employee's shift assignment for one calendar date. A standalone aggregate root linked by
/// <see cref="EmployeeId"/>/<see cref="RosterDate"/> rather than owned by any other entity - the
/// same convention as <see cref="Leave.LeaveRequestDay"/> and <see cref="Holidays.Holiday"/>, since
/// day-processing and the roster admin grid both need to query this table directly by date range.
/// </summary>
public sealed class ShiftRoster : TenantAggregateRoot
{
    private ShiftRoster()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly RosterDate { get; private set; }

    /// <summary>Null when <see cref="IsWeeklyOff"/> is true - a weekly-off day has no shift to work.</summary>
    public Guid? ShiftId { get; private set; }

    public bool IsWeeklyOff { get; private set; }

    public RosterSource Source { get; private set; }

    public static ShiftRoster CreateWorkingDay(Guid employeeId, DateOnly rosterDate, Guid shiftId, RosterSource source) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        RosterDate = rosterDate,
        ShiftId = Guard.NotEmpty(shiftId),
        IsWeeklyOff = false,
        Source = source,
        IsActive = true,
    };

    public static ShiftRoster CreateWeeklyOff(Guid employeeId, DateOnly rosterDate, RosterSource source) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        RosterDate = rosterDate,
        ShiftId = null,
        IsWeeklyOff = true,
        Source = source,
        IsActive = true,
    };
}
