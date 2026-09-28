namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// One change to one auditable entity, raised by <c>TenantAuditSaveChangesInterceptor</c> in the
/// same transaction as the change itself and consumed by Platform into <c>audit.AuditLog</c>.
/// <para>
/// The tenant does not travel on this payload: <see cref="IntegrationEventSubscriber{TDbContext}"/>
/// restores it from <see cref="MessageEnvelope"/> before a handler ever runs, so the consumer reads
/// it from <c>ITenantContext</c> like every other tenant-scoped write. <see cref="ActorUserId"/> and
/// <see cref="CorrelationId"/> are different: the envelope carries both, but a handler is only ever
/// given the deserialized event, never the envelope itself, and an audit row with no actor would be
/// useless for the one question it exists to answer - who did this. They are duplicated here for
/// that reason, sourced from the same <c>ICurrentUser</c> the envelope's own copies come from.
/// </para>
/// </summary>
[MessageType("gggs.platform.audit.entry-recorded")]
public sealed record AuditEntryIntegrationEvent(
    string Service,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValuesJson,
    string? NewValuesJson,
    string? ChangedFieldsCsv,
    Guid? ActorUserId,
    string? CorrelationId) : IntegrationEvent;
