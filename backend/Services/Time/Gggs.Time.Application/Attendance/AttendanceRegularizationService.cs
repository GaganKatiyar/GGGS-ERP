using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Snapshots;
using Gggs.Workflow.Contracts;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Submitting and cancelling attendance regularization requests - the same shape as
/// <c>LeaveRequestService</c> (architecture.md §10.4's worked example), one module over: a request
/// starts a workflow instance rather than approving itself, and only
/// <see cref="AttendanceWorkflowOutcomeConsumer"/> reacting to the workflow's outcome ever changes
/// its status.
/// </summary>
public sealed class AttendanceRegularizationService(
    IAttendanceRegularizationRepository regularizations,
    IEmployeeSnapshotRepository snapshots,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork)
{
    private const string RegularizationWorkflowCode = "ATTENDANCE_REGULARIZATION";
    private const string RegularizationEntityType = "ATTENDANCE_REGULARIZATION";

    public async Task<AttendanceRegularization> SubmitAsync(
        Guid? employeeId,
        DateOnly attendanceDate,
        DateTime requestedIn,
        DateTime requestedOut,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        Guid resolvedEmployeeId = employeeId ?? currentUser.EmployeeId
            ?? throw new ForbiddenException("ATTENDANCE_NO_EMPLOYEE_PROFILE", "Your account has no linked employee profile.");

        EmployeeSnapshot? employee = await snapshots.GetAsync(resolvedEmployeeId, cancellationToken);
        Guard(employee is not null, "ATTENDANCE_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "ATTENDANCE_EMPLOYEE_EXITED", "This employee has already exited.");

        AttendanceRegularization regularization = AttendanceRegularization.Submit(
            resolvedEmployeeId, currentUser.UserId, attendanceDate, requestedIn, requestedOut, reasonCode, remarks);

        regularizations.Add(regularization);

        publisher.Publish(new StartWorkflowIntegrationEvent(
            RegularizationWorkflowCode,
            RegularizationEntityType,
            regularization.Id,
            SourceService: "time",
            InitiatorUserId: currentUser.UserId,
            SubjectEmployeeId: resolvedEmployeeId,
            ContextJson: BuildWorkflowContextJson(attendanceDate, reasonCode),
            CorrelationId: currentUser.CorrelationId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return regularization;
    }

    public async Task CancelAsync(Guid regularizationId, CancellationToken cancellationToken = default)
    {
        AttendanceRegularization regularization = await regularizations.GetAsync(regularizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(AttendanceRegularization), regularizationId);

        regularization.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string BuildWorkflowContextJson(DateOnly attendanceDate, string reasonCode)
        => System.Text.Json.JsonSerializer.Serialize(
            new { attendanceDate = attendanceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), reasonCode });

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
