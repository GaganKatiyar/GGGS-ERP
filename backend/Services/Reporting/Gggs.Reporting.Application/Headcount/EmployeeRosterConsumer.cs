using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Contracts;
using Gggs.Reporting.Domain.Headcount;
using Microsoft.Extensions.Logging;

namespace Gggs.Reporting.Application.Headcount;

/// <summary>
/// Keeps the headcount roster current from Core HR's own events - architecture.md §5.2's pattern,
/// same as every other service's <c>EmployeeSnapshotConsumer</c>, but carrying the org dimensions
/// a headcount fact needs instead of display fields.
/// <para>
/// There is no separate "employee hired" event: a hire is the first
/// <see cref="EmployeeAssignmentChangedIntegrationEvent"/> for that employee (<c>Version == 1</c>).
/// This consumer does not need to special-case that - it only has to keep one current row per
/// employee - but it is the reason the nightly job derives "joiners" from
/// <c>DateOfJoining == SnapshotDate</c> rather than from any event flag.
/// </para>
/// </summary>
public sealed class EmployeeRosterConsumer(
    IEmployeeRosterFactRepository roster, ILogger<EmployeeRosterConsumer> logger)
    : IIntegrationEventConsumer<EmployeeAssignmentChangedIntegrationEvent>,
      IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeAssignmentChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EmployeeRosterFact? existing = await roster.GetAsync(integrationEvent.EmployeeId, cancellationToken);

        if (existing is not null && existing.Version >= integrationEvent.Version)
        {
            logger.LogInformation(
                "Roster row for employee {EmployeeId} is already at version {CurrentVersion}; ignoring stale version {IncomingVersion}.",
                integrationEvent.EmployeeId, existing.Version, integrationEvent.Version);
            return;
        }

        if (existing is null)
        {
            roster.Add(EmployeeRosterFact.Create(
                integrationEvent.EmployeeId,
                integrationEvent.EmployeeCode,
                integrationEvent.Status,
                integrationEvent.LegalEntityId,
                integrationEvent.BranchId,
                integrationEvent.DepartmentId,
                integrationEvent.DateOfJoining,
                integrationEvent.Version));
        }
        else
        {
            existing.Apply(
                integrationEvent.EmployeeCode,
                integrationEvent.Status,
                integrationEvent.LegalEntityId,
                integrationEvent.BranchId,
                integrationEvent.DepartmentId,
                integrationEvent.DateOfJoining,
                integrationEvent.Version);
        }

        // No SaveChangesAsync here: IntegrationEventSubscriber saves once, together with the
        // inbox row, after every handler for this message has run.
    }

    public async Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EmployeeRosterFact? existing = await roster.GetAsync(integrationEvent.EmployeeId, cancellationToken);

        if (existing is null || existing.Version >= integrationEvent.Version)
        {
            return;
        }

        existing.MarkExited(DateOnly.FromDateTime(DateTime.UtcNow), integrationEvent.Version);
    }
}
