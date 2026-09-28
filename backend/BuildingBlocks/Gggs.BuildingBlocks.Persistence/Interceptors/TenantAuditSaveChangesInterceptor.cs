using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gggs.BuildingBlocks.Persistence.Interceptors;

/// <summary>
/// Stamps tenant, audit and soft-delete columns on save, refuses writes that would cross a tenant
/// boundary, and stages one <see cref="AuditEntryIntegrationEvent"/> per auditable entity change.
/// <para>
/// These columns are set here rather than in domain code for two reasons: a handler cannot forget
/// them, and a handler cannot forge them either - nothing in the application layer is trusted to
/// decide which tenant a row belongs to.
/// </para>
/// <para>
/// The audit event is written straight to <c>context</c>'s own outbox table rather than through
/// <c>IIntegrationEventPublisher</c>: that port is registered from a factory that resolves this
/// same <c>TDbContext</c>, and injecting it into this interceptor's constructor would ask the
/// container to build the very <c>DbContext</c> whose interceptor list is still being assembled -
/// a real cycle, not just an awkward one. Using the <c>context</c> the interceptor is already
/// handed avoids it entirely, at the cost of duplicating a few lines of envelope construction that
/// <see cref="OutboxIntegrationEventPublisher"/> also does.
/// </para>
/// </summary>
public sealed class TenantAuditSaveChangesInterceptor(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    GggsPlatformOptions platformOptions,
    TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    /// <summary>
    /// Columns every auditable entity carries that add nothing an audit reader does not already
    /// get from the record's own envelope (tenant, actor, occurred date) or its Action - recording
    /// them in the diff too would just repeat the same four values on every single row.
    /// </summary>
    private static readonly HashSet<string> ExcludedPropertyNames =
    [
        nameof(ITenantOwned.TenantId),
        nameof(IAuditable.CreatedBy),
        nameof(IAuditable.CreatedDate),
        nameof(IAuditable.ModifiedBy),
        nameof(IAuditable.ModifiedDate),
        nameof(ISoftDeletable.IsDeleted),
        nameof(ISoftDeletable.DeletedBy),
        nameof(ISoftDeletable.DeletedDate),
        nameof(IConcurrencyAware.RowVersion),

        // No [Sensitive] attribute system exists yet (architecture.md §16.1 describes one); these
        // two are excluded by name as a pragmatic stand-in until it does, rather than letting a
        // credential hash into the audit trail because nobody built the general mechanism yet.
        "PasswordHash",
        "SecurityStamp",
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Guid actingUserId = currentUser.IsAuthenticated ? currentUser.UserId : SystemUsers.System;

        // A snapshot: entries added below (the audit outbox rows themselves) must not be walked
        // again as if they were more business changes to audit.
        List<EntityEntry> entries = [.. context.ChangeTracker.Entries()];

        foreach (EntityEntry entry in entries)
        {
            EntityState originalState = entry.State;

            if (entry.Entity is ITenantOwned)
            {
                ApplyTenant(entry);
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfPresent(entry, nameof(IAuditable.CreatedBy), actingUserId);
                    SetIfPresent(entry, nameof(IAuditable.CreatedDate), now);
                    break;

                case EntityState.Modified:
                    SetIfPresent(entry, nameof(IAuditable.ModifiedBy), actingUserId);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedDate), now);
                    PreventCreatedColumnTampering(entry);
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletable:
                    // HR data is never hard-deleted by application code: statutory retention,
                    // audit trails and "who changed this" all depend on the row surviving.
                    entry.State = EntityState.Modified;
                    SetIfPresent(entry, nameof(ISoftDeletable.IsDeleted), true);
                    SetIfPresent(entry, nameof(ISoftDeletable.DeletedBy), actingUserId);
                    SetIfPresent(entry, nameof(ISoftDeletable.DeletedDate), now);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedBy), actingUserId);
                    SetIfPresent(entry, nameof(IAuditable.ModifiedDate), now);
                    break;

                default:
                    break;
            }

            // Tenant-owned only, and only with a real tenant resolved: a platform-owned entity
            // (Tenant, Plan, ...) has no TenantId of its own to attribute the row to, and the one
            // moment a Tenant row is created - provisioning - runs with no tenant resolved yet
            // anyway, since the tenant does not exist until that save completes. Platform-level
            // audit is a real gap (architecture.md §16 describes one), just not this one's.
            if (entry.Entity is IAuditable && entry.Entity is ITenantOwned && tenantContext.IsResolved)
            {
                StageAuditEntry(context, entry, originalState, now);
            }
        }
    }

    /// <summary>
    /// Builds and stages one audit outbox message for this entity's change, unless nothing
    /// business-relevant actually changed (a save that only touched <see cref="ExcludedPropertyNames"/>
    /// writes no row - there is nothing there worth reading back).
    /// </summary>
    private void StageAuditEntry(DbContext context, EntityEntry entry, EntityState originalState, DateTime now)
    {
        string action = originalState switch
        {
            EntityState.Added => "Created",
            EntityState.Deleted => "Deleted",
            EntityState.Modified => "Modified",
            _ => null!,
        };

        if (action is null)
        {
            return;
        }

        Dictionary<string, object?>? oldValues = null;
        Dictionary<string, object?>? newValues = null;
        List<string>? changedFields = null;

        foreach (PropertyEntry property in entry.Properties)
        {
            string name = property.Metadata.Name;
            if (ExcludedPropertyNames.Contains(name))
            {
                continue;
            }

            switch (originalState)
            {
                case EntityState.Added:
                    (newValues ??= [])[name] = property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    (oldValues ??= [])[name] = property.OriginalValue;
                    break;

                default: // Modified
                    if (!property.IsModified)
                    {
                        continue;
                    }

                    (changedFields ??= []).Add(name);
                    (oldValues ??= [])[name] = property.OriginalValue;
                    (newValues ??= [])[name] = property.CurrentValue;
                    break;
            }
        }

        if (action == "Modified" && changedFields is null or { Count: 0 })
        {
            // Everything that changed was bookkeeping the reader already gets from the envelope.
            return;
        }

        string entityId = entry.Metadata.FindPrimaryKey()?.Properties is [{ } keyProperty]
            ? entry.Property(keyProperty.Name).CurrentValue?.ToString() ?? "unknown"
            : "unknown";

        var auditEvent = new AuditEntryIntegrationEvent(
            platformOptions.ServiceName,
            action,
            entry.Entity.GetType().Name,
            entityId,
            Serialize(oldValues),
            Serialize(newValues),
            changedFields is { Count: > 0 } ? string.Join(',', changedFields) : null,
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            currentUser.CorrelationId);

        var envelope = new MessageEnvelope
        {
            MessageId = auditEvent.MessageId,
            // Guaranteed resolved: StageAuditEntry is only called when tenantContext.IsResolved.
            TenantId = tenantContext.TenantId,
            MessageType = MessageTypeName.For<AuditEntryIntegrationEvent>(),
            SchemaVersion = auditEvent.SchemaVersion,
            OccurredDate = now,
            CorrelationId = currentUser.CorrelationId,
            ActorUserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
            Payload = JsonSerializer.Serialize(auditEvent, OutboxIntegrationEventPublisher.SerializerOptions),
        };

        context.Set<OutboxMessage>().Add(OutboxMessage.Create(envelope, now));
    }

    private static string? Serialize(Dictionary<string, object?>? values)
        => values is null ? null : JsonSerializer.Serialize(values);

    private void ApplyTenant(EntityEntry entry)
    {
        PropertyEntry tenantProperty = entry.Property(nameof(ITenantOwned.TenantId));
        var entityTenantId = (Guid)(tenantProperty.CurrentValue ?? Guid.Empty);

        if (entry.State == EntityState.Added)
        {
            if (entityTenantId == Guid.Empty)
            {
                // Normal path: the row inherits the tenant of the operation that created it.
                tenantProperty.CurrentValue = tenantContext.TenantId;
                return;
            }

            // A tenant was set explicitly. Only provisioning and migration code may do that, and
            // only inside a platform scope; anywhere else it is an attempt to plant a row in
            // someone else's tenant.
            if (!tenantContext.IsPlatformScope && entityTenantId != tenantContext.TenantId)
            {
                throw new TenantMismatchException(tenantContext.TenantId, entityTenantId, entry.Entity.GetType().Name);
            }

            return;
        }

        if (entry.State is EntityState.Modified or EntityState.Deleted)
        {
            if (tenantProperty.IsModified)
            {
                // Moving a row between tenants is never a business operation.
                var originalTenantId = (Guid)(tenantProperty.OriginalValue ?? Guid.Empty);
                throw new TenantMismatchException(originalTenantId, entityTenantId, entry.Entity.GetType().Name);
            }

            if (!tenantContext.IsPlatformScope && entityTenantId != tenantContext.TenantId)
            {
                throw new TenantMismatchException(tenantContext.TenantId, entityTenantId, entry.Entity.GetType().Name);
            }
        }
    }

    /// <summary>Creation metadata is immutable; silently restore it rather than trusting the update.</summary>
    private static void PreventCreatedColumnTampering(EntityEntry entry)
    {
        foreach (string column in (string[])[nameof(IAuditable.CreatedBy), nameof(IAuditable.CreatedDate)])
        {
            PropertyEntry? property = FindProperty(entry, column);
            if (property?.IsModified == true)
            {
                property.CurrentValue = property.OriginalValue;
                property.IsModified = false;
            }
        }
    }

    private static void SetIfPresent(EntityEntry entry, string propertyName, object? value)
    {
        PropertyEntry? property = FindProperty(entry, propertyName);
        if (property is not null)
        {
            property.CurrentValue = value;
        }
    }

    private static PropertyEntry? FindProperty(EntityEntry entry, string propertyName)
        => entry.Metadata.FindProperty(propertyName) is null ? null : entry.Property(propertyName);
}
