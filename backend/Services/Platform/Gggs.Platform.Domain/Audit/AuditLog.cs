using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Platform.Domain.Audit;

/// <summary>
/// One recorded change or action, read by tenant admins and auditors through
/// <c>/api/v1/audit/logs</c>.
/// <para>
/// The only way a row is created is <c>AuditEntryConsumer</c> handling an
/// <c>AuditEntryIntegrationEvent</c> that arrived from some service's outbox - nothing in this
/// service writes here directly, which is what makes the trail trustworthy: an admin with
/// <c>audit.log.view</c> cannot also grant themselves write access to what they are being watched
/// by.
/// </para>
/// <para>
/// Tenant-owned like any other row a tenant admin can read (the query filter and row-level
/// security apply here too), but deliberately <b>not</b> <see cref="IAuditable"/>: an audit row
/// does not get its own audit trail, and implementing that interface would make
/// <c>TenantAuditSaveChangesInterceptor</c> try to audit the audit log's own insert, on every
/// insert, forever.
/// </para>
/// <para>
/// Append-only. Nothing here updates or deletes a row; the only thing that ever will is the
/// retention job described in architecture.md §16.4, which this slice does not yet build.
/// </para>
/// </summary>
public sealed class AuditLog : AggregateRoot, ITenantOwned
{
    private AuditLog()
    {
        Service = null!;
        Action = null!;
        EntityType = null!;
        EntityId = null!;
    }

    public Guid TenantId { get; private set; }

    /// <summary>When the change happened, not when this row was written - the two can differ by
    /// however long the message sat in the outbox and on the wire.</summary>
    public DateTime OccurredDate { get; private set; }

    /// <summary>Null for a change with no signed-in actor, e.g. a migration or a background job.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Which service raised this entry, e.g. <c>corehr</c>.</summary>
    public string Service { get; private set; }

    /// <summary>Created, Modified or Deleted.</summary>
    public string Action { get; private set; }

    /// <summary>The entity's CLR type name, e.g. <c>LegalEntity</c>.</summary>
    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    /// <summary>JSON. Null for a Created entry - there is no "old" state.</summary>
    public string? OldValuesJson { get; private set; }

    /// <summary>JSON. Null for a Deleted entry - there is no "new" state.</summary>
    public string? NewValuesJson { get; private set; }

    /// <summary>Comma-separated property names. Only meaningful for a Modified entry.</summary>
    public string? ChangedFieldsCsv { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>
    /// <paramref name="messageId"/> becomes this row's own id, so a redelivered message that
    /// somehow slipped past the inbox's own dedup fails on the primary key instead of duplicating
    /// the entry, rather than each retry minting a fresh row for the same event.
    /// </summary>
    public static AuditLog Record(
        Guid tenantId,
        Guid messageId,
        DateTime occurredDate,
        Guid? userId,
        string service,
        string action,
        string entityType,
        string entityId,
        string? oldValuesJson,
        string? newValuesJson,
        string? changedFieldsCsv,
        string? correlationId) => new()
        {
            Id = Guard.NotEmpty(messageId),
            TenantId = Guard.NotEmpty(tenantId),
            OccurredDate = occurredDate,
            UserId = userId,
            Service = Guard.MaxLength(Guard.NotNullOrWhiteSpace(service).Trim(), 50),
            Action = Guard.MaxLength(Guard.NotNullOrWhiteSpace(action).Trim(), 30),
            EntityType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityType).Trim(), 200),
            EntityId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityId).Trim(), 100),
            OldValuesJson = oldValuesJson,
            NewValuesJson = newValuesJson,
            ChangedFieldsCsv = changedFieldsCsv,
            CorrelationId = correlationId,
        };
}
