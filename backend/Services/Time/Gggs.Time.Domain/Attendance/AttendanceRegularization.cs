using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum AttendanceRegularizationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// An employee's request to correct a date's computed attendance (a missed punch, a device
/// failure) - goes through <c>Gggs.Workflow</c> for approval, the same shape as <see cref="Leave.LeaveRequest"/>:
/// no <c>Approve()</c>/<c>Reject()</c> a caller in this service could invoke directly, only
/// <c>AttendanceWorkflowOutcomeConsumer</c> reacting to <c>WorkflowApproved</c>/<c>RejectedIntegrationEvent</c>.
/// </summary>
public sealed class AttendanceRegularization : TenantAggregateRoot
{
    private AttendanceRegularization()
    {
        ReasonCode = null!;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly AttendanceDate { get; private set; }

    public DateTime RequestedIn { get; private set; }

    public DateTime RequestedOut { get; private set; }

    public string ReasonCode { get; private set; }

    public string? Remarks { get; private set; }

    public AttendanceRegularizationStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public Guid AppliedByUserId { get; private set; }

    public static AttendanceRegularization Submit(
        Guid employeeId,
        Guid appliedByUserId,
        DateOnly attendanceDate,
        DateTime requestedIn,
        DateTime requestedOut,
        string reasonCode,
        string? remarks)
    {
        Guard.Against(requestedOut <= requestedIn, "ATTENDANCE_REGULARIZATION_INVALID_RANGE", "The out time must be after the in time.");

        return new AttendanceRegularization
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            AppliedByUserId = Guard.NotEmpty(appliedByUserId),
            AttendanceDate = attendanceDate,
            RequestedIn = requestedIn,
            RequestedOut = requestedOut,
            ReasonCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reasonCode).Trim().ToUpperInvariant(), 30),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : Guard.MaxLength(remarks.Trim(), 1000),
            Status = AttendanceRegularizationStatus.Pending,
            IsActive = true,
        };
    }

    public void LinkWorkflowInstance(Guid workflowInstanceId) => WorkflowInstanceId = Guard.NotEmpty(workflowInstanceId);

    public void MarkApproved()
    {
        Guard.Against(
            Status != AttendanceRegularizationStatus.Pending, "ATTENDANCE_REGULARIZATION_NOT_PENDING", "This request is not pending.");
        Status = AttendanceRegularizationStatus.Approved;
    }

    public void MarkRejected()
    {
        Guard.Against(
            Status != AttendanceRegularizationStatus.Pending, "ATTENDANCE_REGULARIZATION_NOT_PENDING", "This request is not pending.");
        Status = AttendanceRegularizationStatus.Rejected;
    }

    public void Cancel()
    {
        Guard.Against(
            Status != AttendanceRegularizationStatus.Pending,
            "ATTENDANCE_REGULARIZATION_CANNOT_CANCEL",
            "Only a pending request can be cancelled.");

        Status = AttendanceRegularizationStatus.Cancelled;
    }
}
