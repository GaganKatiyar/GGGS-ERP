using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum OvertimeRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// An employee's request to be paid for overtime worked on a date - goes through
/// <c>Gggs.Workflow</c> for approval, the exact same shape as <see cref="AttendanceRegularization"/>:
/// no <c>Approve()</c>/<c>Reject()</c> a caller in this service could invoke directly, only
/// <c>OvertimeWorkflowOutcomeConsumer</c> reacting to <c>WorkflowApproved</c>/<c>RejectedIntegrationEvent</c>.
/// <see cref="ApprovedMinutes"/> lets an approver grant fewer minutes than requested (a manager
/// approving 90 of the 120 minutes claimed) without a second round trip; it is set only on approval
/// and stays null otherwise.
/// </summary>
public sealed class OvertimeRequest : TenantAggregateRoot
{
    private OvertimeRequest()
    {
        ReasonCode = null!;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly OvertimeDate { get; private set; }

    public int RequestedMinutes { get; private set; }

    public string ReasonCode { get; private set; }

    public string? Remarks { get; private set; }

    public OvertimeRequestStatus Status { get; private set; }

    public int? ApprovedMinutes { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public Guid AppliedByUserId { get; private set; }

    public static OvertimeRequest Submit(
        Guid employeeId, Guid appliedByUserId, DateOnly overtimeDate, int requestedMinutes, string reasonCode, string? remarks)
    {
        Guard.Against(requestedMinutes <= 0, "OVERTIME_MINUTES_MUST_BE_POSITIVE", "The requested minutes must be greater than zero.");

        return new OvertimeRequest
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            AppliedByUserId = Guard.NotEmpty(appliedByUserId),
            OvertimeDate = overtimeDate,
            RequestedMinutes = requestedMinutes,
            ReasonCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reasonCode).Trim().ToUpperInvariant(), 30),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : Guard.MaxLength(remarks.Trim(), 1000),
            Status = OvertimeRequestStatus.Pending,
            IsActive = true,
        };
    }

    public void LinkWorkflowInstance(Guid workflowInstanceId) => WorkflowInstanceId = Guard.NotEmpty(workflowInstanceId);

    public void MarkApproved(int approvedMinutes)
    {
        Guard.Against(Status != OvertimeRequestStatus.Pending, "OVERTIME_REQUEST_NOT_PENDING", "This request is not pending.");
        Guard.Against(approvedMinutes <= 0, "OVERTIME_MINUTES_MUST_BE_POSITIVE", "The approved minutes must be greater than zero.");
        Guard.Against(
            approvedMinutes > RequestedMinutes, "OVERTIME_APPROVED_EXCEEDS_REQUESTED", "The approved minutes cannot exceed the requested minutes.");

        Status = OvertimeRequestStatus.Approved;
        ApprovedMinutes = approvedMinutes;
    }

    public void MarkRejected()
    {
        Guard.Against(Status != OvertimeRequestStatus.Pending, "OVERTIME_REQUEST_NOT_PENDING", "This request is not pending.");
        Status = OvertimeRequestStatus.Rejected;
    }

    public void Cancel()
    {
        Guard.Against(
            Status != OvertimeRequestStatus.Pending, "OVERTIME_REQUEST_CANNOT_CANCEL", "Only a pending request can be cancelled.");

        Status = OvertimeRequestStatus.Cancelled;
    }
}
