using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Snapshots;
using Gggs.Workflow.Contracts;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Submitting and cancelling overtime requests - the same shape as
/// <c>AttendanceRegularizationService</c>, one module over: a request starts a workflow instance
/// rather than approving itself, and only <see cref="OvertimeWorkflowOutcomeConsumer"/> reacting to
/// the workflow's outcome ever changes its status.
/// </summary>
public sealed class OvertimeRequestService(
    IOvertimeRequestRepository requests,
    IEmployeeSnapshotRepository snapshots,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork)
{
    private const string OvertimeWorkflowCode = "OVERTIME_REQUEST";
    private const string OvertimeEntityType = "OVERTIME_REQUEST";

    public async Task<OvertimeRequest> SubmitAsync(
        Guid? employeeId, DateOnly overtimeDate, int requestedMinutes, string reasonCode, string? remarks,
        CancellationToken cancellationToken = default)
    {
        Guid resolvedEmployeeId = employeeId ?? currentUser.EmployeeId
            ?? throw new ForbiddenException("ATTENDANCE_NO_EMPLOYEE_PROFILE", "Your account has no linked employee profile.");

        EmployeeSnapshot? employee = await snapshots.GetAsync(resolvedEmployeeId, cancellationToken);
        Guard(employee is not null, "ATTENDANCE_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "ATTENDANCE_EMPLOYEE_EXITED", "This employee has already exited.");

        OvertimeRequest request = OvertimeRequest.Submit(resolvedEmployeeId, currentUser.UserId, overtimeDate, requestedMinutes, reasonCode, remarks);
        requests.Add(request);

        publisher.Publish(new StartWorkflowIntegrationEvent(
            OvertimeWorkflowCode,
            OvertimeEntityType,
            request.Id,
            SourceService: "time",
            InitiatorUserId: currentUser.UserId,
            SubjectEmployeeId: resolvedEmployeeId,
            ContextJson: BuildWorkflowContextJson(overtimeDate, requestedMinutes, reasonCode),
            CorrelationId: currentUser.CorrelationId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        OvertimeRequest request = await requests.GetAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(OvertimeRequest), requestId);

        request.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string BuildWorkflowContextJson(DateOnly overtimeDate, int requestedMinutes, string reasonCode)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            overtimeDate = overtimeDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            requestedMinutes,
            reasonCode,
        });

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
