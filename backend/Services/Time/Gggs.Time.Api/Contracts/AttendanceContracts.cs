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

public sealed record OvertimeRequestResponse(
    Guid Id,
    Guid EmployeeId,
    DateOnly OvertimeDate,
    int RequestedMinutes,
    string ReasonCode,
    string? Remarks,
    OvertimeRequestStatus Status,
    int? ApprovedMinutes,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate);

public sealed record SubmitOvertimeRequestBody(Guid? EmployeeId, DateOnly OvertimeDate, int RequestedMinutes, string ReasonCode, string? Remarks);

public sealed record BiometricDeviceResponse(
    Guid Id, string SerialNumber, string Name, Guid BranchId, BiometricDeviceStatus Status, DateTime? LastSeenDate);

/// <summary>Only ever returned from register/rotate - the plaintext key exists nowhere in the
/// database, so this response is the operator's one and only chance to copy it down.</summary>
public sealed record BiometricDeviceRegisteredResponse(BiometricDeviceResponse Device, string ApiKey);

public sealed record RegisterBiometricDeviceBody(string SerialNumber, string Name, Guid BranchId);

public sealed record DeviceUserMapResponse(Guid Id, Guid DeviceId, string DeviceUserCode, Guid EmployeeId);

public sealed record CreateDeviceUserMapBody(string DeviceUserCode, Guid EmployeeId);

public sealed record DeviceIngestPunchBody(
    string DeviceUserCode, DateTime PunchTimeUtc, PunchDirection Direction, string? ExternalRef);
