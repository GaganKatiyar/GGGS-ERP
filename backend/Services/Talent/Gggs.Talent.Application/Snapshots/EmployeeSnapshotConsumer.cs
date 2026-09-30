using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Contracts;
using Gggs.Talent.Domain.Snapshots;
using Microsoft.Extensions.Logging;

namespace Gggs.Talent.Application.Snapshots;

/// <summary>Keeps this service's local <see cref="EmployeeSnapshot"/> copy current from Core HR's
/// own events - architecture.md §5.2's pattern, same as <c>Gggs.Time.Application.Snapshots.EmployeeSnapshotConsumer</c>
/// and <c>Gggs.Workflow.Application.Snapshots.EmployeeSnapshotConsumer</c>. Talent does not react to
/// an exit specially (a requisition's hiring manager reference is display-only, no eligibility or
/// approval logic depends on it), so unlike those two, this only consumes the assignment-changed event.</summary>
public sealed class EmployeeSnapshotConsumer(
    IEmployeeSnapshotRepository snapshots, ILogger<EmployeeSnapshotConsumer> logger)
    : IIntegrationEventConsumer<EmployeeAssignmentChangedIntegrationEvent>
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
                integrationEvent.EmployeeId, integrationEvent.EmployeeCode, integrationEvent.DisplayName, integrationEvent.Version));
        }
        else
        {
            existing.Apply(integrationEvent.EmployeeCode, integrationEvent.DisplayName, integrationEvent.Version);
        }

        // No SaveChangesAsync here: IntegrationEventSubscriber saves once, together with the
        // inbox row, after every handler for this message has run.
    }
}
