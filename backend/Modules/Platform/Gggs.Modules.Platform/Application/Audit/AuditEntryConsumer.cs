using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Platform.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Platform.Application.Audit;

/// <summary>Platform's own persistence for the audit ledger, implemented against its DbContext.</summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Beyond the inbox's own dedup (see <see cref="AuditEntryConsumer"/>): true if this exact
    /// message was already recorded.
    /// </summary>
    Task<bool> ExistsAsync(Guid auditLogId, CancellationToken cancellationToken = default);

    void Add(AuditLog auditLog);
}

/// <summary>
/// Turns an <see cref="AuditEntryIntegrationEvent"/> arriving from any service's outbox into a row
/// in <c>audit.AuditLog</c>.
/// <para>
/// This is the one and only writer of that table (see <see cref="AuditLog"/>'s own remarks), and it
/// runs inside <c>Gggs.Platform.Worker</c> - a process with no signed-in user of its own - which is
/// exactly why the actor has to travel on the event rather than come from <c>ICurrentUser</c> here:
/// this handler's caller is the message bus, not the person who made the original change.
/// </para>
/// </summary>
public sealed class AuditEntryConsumer(
    IAuditLogRepository auditLogs,
    ITenantContext tenantContext,
    ILogger<AuditEntryConsumer> logger) : IIntegrationEventConsumer<AuditEntryIntegrationEvent>
{
    public async Task HandleAsync(
        AuditEntryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (await auditLogs.ExistsAsync(integrationEvent.MessageId, cancellationToken))
        {
            logger.LogInformation(
                "Audit entry {MessageId} was already recorded; skipping.",
                integrationEvent.MessageId);
            return;
        }

        auditLogs.Add(AuditLog.Record(
            tenantContext.TenantId,
            integrationEvent.MessageId,
            integrationEvent.OccurredDate,
            integrationEvent.ActorUserId,
            integrationEvent.Service,
            integrationEvent.Action,
            integrationEvent.EntityType,
            integrationEvent.EntityId,
            integrationEvent.OldValuesJson,
            integrationEvent.NewValuesJson,
            integrationEvent.ChangedFieldsCsv,
            integrationEvent.CorrelationId));

        // No SaveChangesAsync here: IntegrationEventSubscriber saves once, after every handler for
        // this message has run, together with the inbox row - so this insert and the inbox
        // marking commit in the same transaction, the same "both or neither" guarantee the outbox
        // gives on the way out.
        logger.LogInformation(
            "Recorded a {Action} audit entry for {EntityType} {EntityId} from {Service}.",
            integrationEvent.Action,
            integrationEvent.EntityType,
            integrationEvent.EntityId,
            integrationEvent.Service);
    }
}
