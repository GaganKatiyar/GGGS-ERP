using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Attendance.Domain;

public enum PunchSource
{
    Web = 1,
    Device = 2,
    Regularization = 3,
    Import = 4,
}

/// <summary>
/// One clock-in or clock-out, stored as the instant it happened (UTC). Direction is not recorded:
/// a day is judged by its first and last punch, which is what devices report reliably.
/// </summary>
public sealed class AttendancePunch : TenantAggregateRoot
{
    private AttendancePunch()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateTime PunchedAt { get; private set; }

    public PunchSource Source { get; private set; }

    public Guid? DeviceId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? Note { get; private set; }

    public static AttendancePunch Record(Guid employeeId, DateTime punchedAtUtc, PunchSource source, Guid? deviceId = null, string? ipAddress = null, string? note = null) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        PunchedAt = DateTime.SpecifyKind(punchedAtUtc, DateTimeKind.Utc),
        Source = source,
        DeviceId = deviceId,
        IpAddress = ipAddress is null ? null : Guard.MaxLength(ipAddress, 64),
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 200),
    };
}

public enum AttendanceStatus
{
    Present = 1,
    HalfDay = 2,
    Absent = 3,
    WeeklyOff = 4,
    Holiday = 5,
    OnLeave = 6,

    /// <summary>Only one punch: in without out, or the reverse.</summary>
    MissingPunch = 7,
}

/// <summary>
/// A judged day, kept for reports and payroll. It is derived - from punches, the roster, leave and
/// holidays - and recalculated whenever any of those change, so it is never edited directly.
/// </summary>
public sealed class AttendanceDay : TenantAggregateRoot
{
    private AttendanceDay()
    {
        ShiftName = null!;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public string ShiftName { get; private set; }

    public DateTime? FirstIn { get; private set; }

    public DateTime? LastOut { get; private set; }

    public int WorkedMinutes { get; private set; }

    public int LateMinutes { get; private set; }

    public int EarlyLeaveMinutes { get; private set; }

    public AttendanceStatus Status { get; private set; }

    public int PunchCount { get; private set; }

    public bool WorkedOnDayOff { get; private set; }

    public bool IsRegularized { get; private set; }

    public string? Note { get; private set; }

    public static AttendanceDay For(Guid employeeId, DateOnly date) => new() { EmployeeId = Guard.NotEmpty(employeeId), Date = date, ShiftName = string.Empty };

    public void Apply(DayEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ShiftName = Guard.MaxLength(evaluation.ShiftName, 100);
        FirstIn = evaluation.FirstIn;
        LastOut = evaluation.LastOut;
        WorkedMinutes = evaluation.WorkedMinutes;
        LateMinutes = evaluation.LateMinutes;
        EarlyLeaveMinutes = evaluation.EarlyLeaveMinutes;
        Status = evaluation.Status;
        PunchCount = evaluation.PunchCount;
        WorkedOnDayOff = evaluation.WorkedOnDayOff;
        IsRegularized = evaluation.IsRegularized;
        Note = evaluation.Note is null ? null : Guard.MaxLength(evaluation.Note, 200);
    }
}

public enum RegularizationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>"I forgot to punch": the times someone says they worked, recorded once approved.</summary>
public sealed class RegularizationRequest : TenantAggregateRoot
{
    private RegularizationRequest()
    {
        Reason = null!;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Local wall-clock times on <see cref="Date"/>; check-out may be after midnight for a night shift.</summary>
    public TimeOnly? CheckIn { get; private set; }

    public TimeOnly? CheckOut { get; private set; }

    public string Reason { get; private set; }

    public RegularizationStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public static RegularizationRequest Create(Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut, string reason)
    {
        if (checkIn is null && checkOut is null)
        {
            throw new DomainRuleException("REGULARIZATION_EMPTY", "Give the check-in time, the check-out time, or both.");
        }

        return new RegularizationRequest
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            Date = date,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 300),
            Status = RegularizationStatus.Pending,
        };
    }

    public void AwaitApproval(Guid instanceId) => WorkflowInstanceId = instanceId;

    public void Approve() => Move(RegularizationStatus.Approved);

    public void Reject() => Move(RegularizationStatus.Rejected);

    public void Cancel() => Move(RegularizationStatus.Cancelled);

    private void Move(RegularizationStatus status)
    {
        if (Status != RegularizationStatus.Pending)
        {
            throw new DomainRuleException("REGULARIZATION_CLOSED", $"This request is already {Status}.");
        }

        Status = status;
    }
}

/// <summary>A biometric or card reader whose punches are imported.</summary>
public sealed class AttendanceDevice : TenantAggregateRoot
{
    private AttendanceDevice()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public Guid? LocationId { get; private set; }

    public DateTime? LastSeenDate { get; private set; }

    public static AttendanceDevice Create(string code, string name, Guid? locationId)
    {
        var device = new AttendanceDevice { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32) };
        device.Update(name, locationId);
        return device;
    }

    public void Update(string name, Guid? locationId)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        LocationId = locationId;
    }

    public void Seen(DateTime now) => LastSeenDate = now;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>What the evaluator decided about one day.</summary>
public sealed record DayEvaluation(
    Guid EmployeeId,
    DateOnly Date,
    string ShiftName,
    DateTime? FirstIn,
    DateTime? LastOut,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    AttendanceStatus Status,
    int PunchCount,
    bool WorkedOnDayOff,
    bool IsRegularized,
    string? Note);

/// <summary>The day as scheduled, in the tenant's local time.</summary>
public sealed record DaySchedule(
    string ShiftName,
    DateTime ScheduledStart,
    DateTime ScheduledEnd,
    int BreakMinutes,
    int GraceMinutes,
    int HalfDayMinutes,
    int FullDayMinutes,
    bool IsWeeklyOff);

/// <summary>A punch as the evaluator sees it: local wall-clock time, and whether it came from a regularization.</summary>
public sealed record LocalPunch(DateTime Local, DateTime Utc, bool IsRegularization);

/// <summary>
/// Judges a day (spec §18 rules). Leave and holidays come first; otherwise the first and last punch
/// decide: worked time (less the break), lateness against the start plus grace, leaving early
/// against the end, and present, half-day or absent by the shift's thresholds - halved on a day
/// with half-day leave.
/// </summary>
public static class AttendanceEvaluator
{
    public static DayEvaluation Evaluate(
        Guid employeeId,
        DateOnly date,
        DaySchedule schedule,
        IReadOnlyList<LocalPunch> punches,
        bool fullDayLeave,
        bool halfDayLeave,
        string? holidayName)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(punches);

        List<LocalPunch> ordered = [.. punches.OrderBy(punch => punch.Local)];
        bool regularized = ordered.Any(punch => punch.IsRegularization);
        bool dayOff = schedule.IsWeeklyOff || holidayName is not null;

        DayEvaluation Result(AttendanceStatus status, int worked = 0, int late = 0, int early = 0, string? note = null)
            => new(
                employeeId, date, schedule.ShiftName,
                ordered.Count > 0 ? ordered[0].Utc : null,
                ordered.Count > 1 ? ordered[^1].Utc : null,
                worked, late, early, status, ordered.Count, dayOff && ordered.Count > 1, regularized, note);

        if (fullDayLeave)
        {
            return Result(AttendanceStatus.OnLeave);
        }

        if (ordered.Count == 0)
        {
            if (holidayName is not null)
            {
                return Result(AttendanceStatus.Holiday, note: holidayName);
            }

            return schedule.IsWeeklyOff ? Result(AttendanceStatus.WeeklyOff) : Result(AttendanceStatus.Absent, note: halfDayLeave ? "Half-day leave" : null);
        }

        if (ordered.Count == 1)
        {
            return Result(AttendanceStatus.MissingPunch, note: "Only one punch recorded");
        }

        DateTime first = ordered[0].Local;
        DateTime last = ordered[^1].Local;
        int span = (int)(last - first).TotalMinutes;
        int worked = span > schedule.BreakMinutes + 60 ? span - schedule.BreakMinutes : span;

        int late = dayOff ? 0 : Math.Max(0, (int)(first - schedule.ScheduledStart.AddMinutes(schedule.GraceMinutes)).TotalMinutes);
        int early = dayOff ? 0 : Math.Max(0, (int)(schedule.ScheduledEnd - last).TotalMinutes);

        int full = halfDayLeave ? schedule.FullDayMinutes / 2 : schedule.FullDayMinutes;
        int half = halfDayLeave ? schedule.HalfDayMinutes / 2 : schedule.HalfDayMinutes;

        AttendanceStatus status = worked >= full ? AttendanceStatus.Present
            : worked >= half ? AttendanceStatus.HalfDay
            : AttendanceStatus.Absent;

        if (dayOff && status == AttendanceStatus.Absent)
        {
            status = holidayName is not null ? AttendanceStatus.Holiday : AttendanceStatus.WeeklyOff;
        }

        return Result(status, worked, late, early, halfDayLeave ? "Half-day leave" : holidayName);
    }
}
