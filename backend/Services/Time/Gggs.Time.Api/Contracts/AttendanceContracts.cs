using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Api.Contracts;

public sealed record ShiftResponse(
    Guid Id, string Code, string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, bool IsNightShift,
    int GraceInMinutes, int GraceOutMinutes, int FullDayMinMinutes, int HalfDayMinMinutes);

public sealed record CreateShiftBody(
    string Code, string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, bool IsNightShift,
    int GraceInMinutes, int GraceOutMinutes, int FullDayMinMinutes, int HalfDayMinMinutes);

public sealed record ShiftRosterResponse(
    Guid Id, Guid EmployeeId, DateOnly RosterDate, Guid? ShiftId, bool IsWeeklyOff, RosterSource Source);

public sealed record AssignShiftRosterBody(Guid EmployeeId, DateOnly RosterDate, Guid ShiftId);

public sealed record AssignWeeklyOffBody(Guid EmployeeId, DateOnly RosterDate);

public sealed record AttendancePunchResponse(
    Guid Id, Guid EmployeeId, DateTime PunchTimeUtc, PunchDirection Direction, PunchSource Source, bool IsValid);

public sealed record RecordPunchBody(
    Guid? EmployeeId,
    DateTime PunchTimeUtc,
    PunchDirection Direction,
    PunchSource Source,
    Guid? DeviceId,
    decimal? Latitude,
    decimal? Longitude,
    string? ExternalRef);

public sealed record AttendanceDayResponse(
    Guid Id,
    Guid EmployeeId,
    DateOnly AttendanceDate,
    Guid? ShiftId,
    DateTime? FirstIn,
    DateTime? LastOut,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyOutMinutes,
    AttendanceDayStatus Status,
    Guid? LeaveRequestId,
    bool IsRegularized,
    bool IsLocked);

public sealed record AttendanceRegularizationResponse(
    Guid Id,
    Guid EmployeeId,
    DateOnly AttendanceDate,
    DateTime RequestedIn,
    DateTime RequestedOut,
    string ReasonCode,
    string? Remarks,
    AttendanceRegularizationStatus Status,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate);

public sealed record SubmitRegularizationBody(
    Guid? EmployeeId, DateOnly AttendanceDate, DateTime RequestedIn, DateTime RequestedOut, string ReasonCode, string? Remarks);
