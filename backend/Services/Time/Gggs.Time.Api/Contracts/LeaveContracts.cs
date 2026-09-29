using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Api.Contracts;

public sealed record LeaveTypeResponse(
    Guid Id, string Code, string Name, bool IsPaid, LeaveUnit Unit, string? GenderRestrictionCode,
    bool IsCompOff, bool IsLop, string ColorHex);

public sealed record CreateLeaveTypeBody(
    string Code, string Name, bool IsPaid, LeaveUnit Unit, string? GenderRestrictionCode,
    bool IsCompOff, bool IsLop, string ColorHex);

public sealed record LeavePolicyResponse(Guid Id, string Name, int Version);

public sealed record CreateLeavePolicyBody(string Name);

public sealed record LeavePolicyRuleResponse(
    Guid Id, Guid PolicyId, Guid LeaveTypeId, AccrualFrequency AccrualFrequency, decimal AccrualAmount,
    bool ProRataOnJoining, decimal? MaxBalance, decimal? CarryForwardMax, int? CarryForwardExpiryMonths,
    bool EncashmentAllowed, int EligibleAfterDays, int MinNoticeDays, int? MaxConsecutiveDays,
    decimal MinDaysPerRequest, bool AllowHalfDay, bool AllowNegativeBalance, decimal NegativeLimit);

public sealed record CreateLeavePolicyRuleBody(
    Guid LeaveTypeId, AccrualFrequency AccrualFrequency, decimal AccrualAmount, bool ProRataOnJoining,
    decimal? MaxBalance, decimal? CarryForwardMax, int? CarryForwardExpiryMonths, bool EncashmentAllowed,
    int EligibleAfterDays, int MinNoticeDays, int? MaxConsecutiveDays, decimal MinDaysPerRequest,
    bool AllowHalfDay, bool AllowNegativeBalance, decimal NegativeLimit);

public sealed record LeaveBalanceResponse(
    Guid LeaveTypeId, int PeriodYear, decimal Opening, decimal Accrued, decimal Taken, decimal PendingApproval,
    decimal Adjusted, decimal Encashed, decimal Lapsed, decimal CarriedForward, decimal Available);

public sealed record LeaveRequestResponse(
    Guid Id, string RequestNo, Guid EmployeeId, Guid LeaveTypeId, DateOnly FromDate, LeaveDaySession FromSession,
    DateOnly ToDate, LeaveDaySession ToSession, decimal TotalDays, string? Reason, LeaveRequestStatus Status,
    Guid? WorkflowInstanceId, DateTime CreatedDate);

public sealed record LeaveRequestListItem(
    Guid Id, string RequestNo, Guid EmployeeId, Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate,
    decimal TotalDays, LeaveRequestStatus Status);

public sealed record CreateLeaveRequestBody(
    Guid? EmployeeId, Guid LeaveTypeId, DateOnly FromDate, LeaveDaySession FromSession,
    DateOnly ToDate, LeaveDaySession ToSession, string? Reason, Guid? AttachmentDocumentId);
