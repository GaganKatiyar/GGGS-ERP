using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Leave;

public enum LeaveDaySession
{
    Full = 1,
    First = 2,
    Second = 3,
}

public enum LeaveRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Withdrawn = 5,
}

/// <summary>
/// One employee's application for time off - goes through <c>Gggs.Workflow</c> for approval
/// (architecture.md §13.3), never approves itself. <see cref="Status"/> stays
/// <see cref="LeaveRequestStatus.Pending"/> until <c>LeaveWorkflowOutcomeConsumer</c> reacts to a
/// <c>WorkflowApproved</c>/<c>RejectedIntegrationEvent</c> - this aggregate has no
/// <c>Approve()</c>/<c>Reject()</c> that a caller in this service could invoke directly, precisely
/// so the only path to "Approved" is through the workflow that was actually run.
/// </summary>
public sealed class LeaveRequest : TenantAggregateRoot
{
    private LeaveRequest()
    {
        RequestNo = null!;
    }

    public string RequestNo { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public DateOnly FromDate { get; private set; }

    public LeaveDaySession FromSession { get; private set; }

    public DateOnly ToDate { get; private set; }

    public LeaveDaySession ToSession { get; private set; }

    public decimal TotalDays { get; private set; }

    public string? Reason { get; private set; }

    public Guid? AttachmentDocumentId { get; private set; }

    public LeaveRequestStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>Who actually submitted this - the employee themselves, or an HR admin applying on
    /// their behalf (<c>leave.request.create-on-behalf</c>). Not necessarily the employee's own
    /// linked user.</summary>
    public Guid AppliedByUserId { get; private set; }

    /// <summary>
    /// No navigation to its <see cref="LeaveRequestDay"/> rows: the two are separate aggregate
    /// roots linked by <see cref="LeaveRequestDay.LeaveRequestId"/>, the same shape as
    /// <c>Employee</c>/<c>EmploymentRecord</c> in Core HR, rather than a parent-owned child
    /// collection - this codebase's only precedent for a "many" side (<c>Role.Permissions</c>) is
    /// an owned JSON column, not a real relational table, and <see cref="LeaveRequestDay"/> has to
    /// be its own independently-queryable table (the overlap check and the future team calendar
    /// both read it by date, across every request). <see cref="LeaveRequestService.SubmitAsync"/>
    /// builds the day rows itself, against this request's own <see cref="Entity.Id"/>, which is
    /// already stable the moment <see cref="Submit"/> returns (a client-generated sequential GUID,
    /// not one SQL Server assigns on insert).
    /// </summary>
    public static LeaveRequest Submit(
        string requestNo,
        Guid employeeId,
        Guid appliedByUserId,
        Guid leaveTypeId,
        DateOnly fromDate,
        LeaveDaySession fromSession,
        DateOnly toDate,
        LeaveDaySession toSession,
        string? reason,
        Guid? attachmentDocumentId,
        decimal totalDays)
    {
        Guard.Against(toDate < fromDate, "LEAVE_REQUEST_INVALID_RANGE", "The end date cannot be before the start date.");
        Guard.Against(totalDays <= 0, "LEAVE_REQUEST_NO_WORK_DAYS", "The selected range has no working days to apply leave against.");

        return new LeaveRequest
        {
            RequestNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(requestNo), 30),
            EmployeeId = Guard.NotEmpty(employeeId),
            AppliedByUserId = Guard.NotEmpty(appliedByUserId),
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            FromDate = fromDate,
            FromSession = fromSession,
            ToDate = toDate,
            ToSession = toSession,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 1000),
            AttachmentDocumentId = attachmentDocumentId,
            Status = LeaveRequestStatus.Pending,
            TotalDays = totalDays,
            IsActive = true,
        };
    }

    /// <summary>Set once the workflow instance actually starts - see
    /// <c>LeaveRequestService.SubmitAsync</c>'s own remarks on why this is a separate step from
    /// <see cref="Submit"/> rather than folded into it.</summary>
    public void LinkWorkflowInstance(Guid workflowInstanceId) => WorkflowInstanceId = Guard.NotEmpty(workflowInstanceId);

    public void MarkApproved()
    {
        Guard.Against(Status != LeaveRequestStatus.Pending, "LEAVE_REQUEST_NOT_PENDING", "This request is not pending.");
        Status = LeaveRequestStatus.Approved;
    }

    public void MarkRejected()
    {
        Guard.Against(Status != LeaveRequestStatus.Pending, "LEAVE_REQUEST_NOT_PENDING", "This request is not pending.");
        Status = LeaveRequestStatus.Rejected;
    }

    public void Cancel()
    {
        Guard.Against(
            Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved),
            "LEAVE_REQUEST_CANNOT_CANCEL",
            "Only a pending or approved request can be cancelled.");

        Status = LeaveRequestStatus.Cancelled;
    }
}

/// <summary>One calendar date within a <see cref="LeaveRequest"/>'s range - a separate row per date
/// rather than just a from/to pair, because a half-day at either end means the per-date fraction
/// is not uniform across the range, and the team calendar/overlap check reads this table by date.</summary>
public sealed class LeaveRequestDay : TenantAggregateRoot
{
    private LeaveRequestDay()
    {
    }

    public Guid LeaveRequestId { get; private set; }

    public DateOnly LeaveDate { get; private set; }

    public decimal DayFraction { get; private set; }

    public static LeaveRequestDay Create(Guid leaveRequestId, DateOnly leaveDate, decimal dayFraction) => new()
    {
        LeaveRequestId = Guard.NotEmpty(leaveRequestId),
        LeaveDate = leaveDate,
        DayFraction = dayFraction,
        IsActive = true,
    };
}
