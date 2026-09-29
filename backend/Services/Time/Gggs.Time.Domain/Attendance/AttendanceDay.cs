using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum AttendanceDayStatus
{
    Present = 1,
    Absent = 2,
    HalfDay = 3,
    OnLeave = 4,
    WeeklyOff = 5,
    Holiday = 6,
    OnDuty = 7,
}

/// <summary>
/// One employee's processed attendance for one calendar date - the projection
/// <see cref="AttendanceDayProcessor"/> rebuilds from that date's roster, punches and leave/holiday
/// state, the same "always recompute from source, never increment in place" shape as
/// <see cref="Leave.LeaveBalance"/>. Recomputed synchronously whenever a punch lands for that date
/// (v1 has no batch job - see <see cref="Recompute"/>'s own remarks), so the row a caller reads is
/// never more than one punch stale.
/// </summary>
public sealed class AttendanceDay : TenantAggregateRoot
{
    private AttendanceDay()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly AttendanceDate { get; private set; }

    public Guid? ShiftId { get; private set; }

    public DateTime? FirstIn { get; private set; }

    public DateTime? LastOut { get; private set; }

    public int WorkedMinutes { get; private set; }

    public int LateMinutes { get; private set; }

    public int EarlyOutMinutes { get; private set; }

    public AttendanceDayStatus Status { get; private set; }

    /// <summary>Set when <see cref="Status"/> is <see cref="AttendanceDayStatus.OnLeave"/> - which
    /// approved <c>LeaveRequest</c> covers this date, so the two records can be cross-referenced.</summary>
    public Guid? LeaveRequestId { get; private set; }

    /// <summary>True once an <see cref="AttendanceRegularization"/> has been approved for this
    /// date - <see cref="Recompute"/> never clears it back to false, since the correction stays
    /// true to history even if the day is later recomputed again for an unrelated reason.</summary>
    public bool IsRegularized { get; private set; }

    /// <summary>True once payroll has locked the period this date falls in - <see cref="Recompute"/>
    /// refuses to run against a locked day (architecture.md's payroll-lock concern for §6.2's
    /// standard columns), so a late-arriving punch or import cannot silently change pay that has
    /// already been calculated.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Bumped on every recompute, purely as an observability aid (how many times has this
    /// row been recalculated) - no code branches on it.</summary>
    public int ComputedVersion { get; private set; }

    public static AttendanceDay CreateEmpty(Guid employeeId, DateOnly attendanceDate) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        AttendanceDate = attendanceDate,
        Status = AttendanceDayStatus.Absent,
        ComputedVersion = 0,
        IsActive = true,
    };

    /// <summary>
    /// Rebuilds every computed field from the day's own inputs. Order of precedence, highest first:
    /// an approved leave request covering this date; a holiday; a weekly off (roster says no shift);
    /// otherwise the shift's punches decide Present/HalfDay/Absent. Mirrors
    /// <see cref="Leave.LeaveBalance.Recalculate"/>'s reasoning almost exactly - a status this many
    /// precedence rules deep drifts if anything ever nudges a field instead of recomputing it whole.
    /// </summary>
    public void Recompute(
        Guid? shiftId,
        Shift? shift,
        bool isWeeklyOff,
        bool isHoliday,
        Guid? approvedLeaveRequestId,
        IReadOnlyList<AttendancePunch> validPunchesForDate)
    {
        Guard.Against(IsLocked, "ATTENDANCE_DAY_LOCKED", "This attendance date is locked for payroll and cannot be recomputed.");

        ComputedVersion++;

        if (approvedLeaveRequestId is { } leaveRequestId)
        {
            ShiftId = shiftId;
            FirstIn = null;
            LastOut = null;
            WorkedMinutes = 0;
            LateMinutes = 0;
            EarlyOutMinutes = 0;
            Status = AttendanceDayStatus.OnLeave;
            LeaveRequestId = leaveRequestId;
            return;
        }

        LeaveRequestId = null;

        if (isHoliday)
        {
            ShiftId = shiftId;
            FirstIn = null;
            LastOut = null;
            WorkedMinutes = 0;
            LateMinutes = 0;
            EarlyOutMinutes = 0;
            Status = AttendanceDayStatus.Holiday;
            return;
        }

        if (isWeeklyOff)
        {
            ShiftId = shiftId;
            FirstIn = null;
            LastOut = null;
            WorkedMinutes = 0;
            LateMinutes = 0;
            EarlyOutMinutes = 0;
            Status = AttendanceDayStatus.WeeklyOff;
            return;
        }

        ShiftId = shiftId;

        List<AttendancePunch> ordered = validPunchesForDate.OrderBy(p => p.PunchTimeUtc).ToList();

        if (ordered.Count == 0)
        {
            FirstIn = null;
            LastOut = null;
            WorkedMinutes = 0;
            LateMinutes = 0;
            EarlyOutMinutes = 0;
            Status = AttendanceDayStatus.Absent;
            return;
        }

        FirstIn = ordered[0].PunchTimeUtc;
        LastOut = ordered[^1].PunchTimeUtc;
        WorkedMinutes = ordered.Count >= 2 ? (int)(LastOut!.Value - FirstIn!.Value).TotalMinutes : 0;

        if (shift is null)
        {
            // Punches with no assigned shift to judge them against: worked time is still counted,
            // but lateness/early-out have nothing to compare to and the full/half-day thresholds
            // fall back to "any work at all counts as present" rather than guessing a shift.
            LateMinutes = 0;
            EarlyOutMinutes = 0;
            Status = WorkedMinutes > 0 ? AttendanceDayStatus.Present : AttendanceDayStatus.Absent;
            return;
        }

        DateTime scheduledStart = AttendanceDate.ToDateTime(shift.StartTime, DateTimeKind.Utc);
        DateTime scheduledEnd = shift.IsNightShift
            ? AttendanceDate.AddDays(1).ToDateTime(shift.EndTime, DateTimeKind.Utc)
            : AttendanceDate.ToDateTime(shift.EndTime, DateTimeKind.Utc);

        int lateMinutes = (int)(FirstIn!.Value - scheduledStart).TotalMinutes - shift.GraceInMinutes;
        LateMinutes = Math.Max(0, lateMinutes);

        int earlyOutMinutes = (int)(scheduledEnd - LastOut!.Value).TotalMinutes - shift.GraceOutMinutes;
        EarlyOutMinutes = Math.Max(0, earlyOutMinutes);

        Status = WorkedMinutes >= shift.FullDayMinMinutes
            ? AttendanceDayStatus.Present
            : WorkedMinutes >= shift.HalfDayMinMinutes
                ? AttendanceDayStatus.HalfDay
                : AttendanceDayStatus.Absent;
    }

    /// <summary>Applied by an approved <see cref="AttendanceRegularization"/>: overrides the
    /// computed in/out times directly rather than routing through <see cref="Recompute"/>, since a
    /// regularization exists precisely because the punch data itself was wrong or missing - there
    /// is nothing left to recompute from.</summary>
    public void ApplyRegularization(DateTime requestedIn, DateTime requestedOut)
    {
        Guard.Against(IsLocked, "ATTENDANCE_DAY_LOCKED", "This attendance date is locked for payroll and cannot be regularized.");
        Guard.Against(requestedOut <= requestedIn, "ATTENDANCE_REGULARIZATION_INVALID_RANGE", "The out time must be after the in time.");

        FirstIn = requestedIn;
        LastOut = requestedOut;
        WorkedMinutes = (int)(requestedOut - requestedIn).TotalMinutes;
        LateMinutes = 0;
        EarlyOutMinutes = 0;
        Status = AttendanceDayStatus.Present;
        IsRegularized = true;
        ComputedVersion++;
    }

    public void Lock() => IsLocked = true;
}
