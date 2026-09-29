using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Domain.Attendance;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Architecture.md §13.3 step 4, attendance's reaction to a workflow instance completing:
/// approval applies the requested in/out times to that date's <see cref="AttendanceDay"/>
/// (<see cref="AttendanceDay.ApplyRegularization"/>); rejection just flips the request's status,
/// since nothing about the day's own computed state was ever touched for it to revert. Same shape
/// as <c>LeaveWorkflowOutcomeConsumer</c>, one module over.
/// </summary>
public sealed class AttendanceWorkflowOutcomeConsumer(
    IAttendanceRegularizationRepository regularizations,
    IAttendanceDayRepository days,
    ILogger<AttendanceWorkflowOutcomeConsumer> logger)
    : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>,
      IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    private const string RegularizationEntityType = "ATTENDANCE_REGULARIZATION";

    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != RegularizationEntityType)
        {
            return;
        }

        AttendanceRegularization? regularization = await regularizations.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (regularization is null)
        {
            logger.LogWarning("WorkflowApproved for unknown attendance regularization {RegularizationId}.", integrationEvent.EntityId);
            return;
        }

        if (regularization.Status != AttendanceRegularizationStatus.Pending)
        {
            logger.LogInformation(
                "Attendance regularization {RegularizationId} is already {Status}; ignoring a duplicate WorkflowApproved.",
                regularization.Id, regularization.Status);
            return;
        }

        regularization.LinkWorkflowInstance(integrationEvent.InstanceId);
        regularization.MarkApproved();

        AttendanceDay? day = await days.GetAsync(regularization.EmployeeId, regularization.AttendanceDate, cancellationToken);

        if (day is null)
        {
            day = AttendanceDay.CreateEmpty(regularization.EmployeeId, regularization.AttendanceDate);
            days.Add(day);
        }

        day.ApplyRegularization(regularization.RequestedIn, regularization.RequestedOut);

        logger.LogInformation(
            "Attendance regularization {RegularizationId} approved for {AttendanceDate}.",
            regularization.Id, regularization.AttendanceDate);
    }

    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.EntityType != RegularizationEntityType)
        {
            return;
        }

        AttendanceRegularization? regularization = await regularizations.GetAsync(integrationEvent.EntityId, cancellationToken);

        if (regularization is null || regularization.Status != AttendanceRegularizationStatus.Pending)
        {
            return;
        }

        regularization.LinkWorkflowInstance(integrationEvent.InstanceId);
        regularization.MarkRejected();

        logger.LogInformation("Attendance regularization {RegularizationId} rejected.", regularization.Id);
    }
}
