using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Contracts;
using Gggs.Workflow.Domain.Snapshots;
using Microsoft.Extensions.Logging;

namespace Gggs.Workflow.Application.Snapshots;

/// <summary>
/// Keeps this service's local <see cref="EmployeeSnapshot"/> copy current from Core HR's own
/// events - architecture.md §5.2's pattern, and this service's first consumer of it.
/// </summary>
public sealed class EmployeeSnapshotConsumer(
    IEmployeeSnapshotRepository snapshots, ILogger<EmployeeSnapshotConsumer> logger)
    : IIntegrationEventConsumer<EmployeeAssignmentChangedIntegrationEvent>,
      IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeAssignmentChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EmployeeSnapshot? existing = await snapshots.GetAsync(integrationEvent.EmployeeId, cancellationToken);

        if (existing is not null && existing.Version >= integrationEvent.Version)
        {
            logger.LogInformation(
                "Snapshot for employee {EmployeeId} is already at version {CurrentVersion}; ignoring stale version {IncomingVersion}.",
                integrationEvent.EmployeeId, existing.Version, integrationEvent.Version);
            return;
        }

        if (existing is null)
        {
            snapshots.Add(EmployeeSnapshot.Create(
                integrationEvent.EmployeeId,
                integrationEvent.EmployeeCode,
                integrationEvent.DisplayName,
                integrationEvent.Status,
                integrationEvent.UserId,
                integrationEvent.LegalEntityId,
                integrationEvent.BranchId,
                integrationEvent.DepartmentId,
                integrationEvent.DesignationId,
                integrationEvent.GradeId,
                integrationEvent.ReportingManagerId,
                integrationEvent.Version));
        }
        else
        {
            existing.Apply(
                integrationEvent.EmployeeCode,
                integrationEvent.DisplayName,
                integrationEvent.Status,
                integrationEvent.UserId,
                integrationEvent.LegalEntityId,
                integrationEvent.BranchId,
                integrationEvent.DepartmentId,
                integrationEvent.DesignationId,
                integrationEvent.GradeId,
                integrationEvent.ReportingManagerId,
                integrationEvent.Version);
        }

        // No SaveChangesAsync here: IntegrationEventSubscriber saves once, together with the
        // inbox row, after every handler for this message has run.
    }

    public async Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EmployeeSnapshot? existing = await snapshots.GetAsync(integrationEvent.EmployeeId, cancellationToken);

        if (existing is null || existing.Version >= integrationEvent.Version)
        {
            return;
        }

        existing.MarkExited(integrationEvent.Version);
    }
}
