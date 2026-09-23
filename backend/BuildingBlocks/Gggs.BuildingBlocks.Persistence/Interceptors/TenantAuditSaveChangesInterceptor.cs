using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gggs.BuildingBlocks.Persistence.Interceptors;

/// <summary>
/// Stamps tenant, audit and soft-delete columns on save, and refuses writes that would cross a
/// tenant boundary.
/// <para>
/// These columns are set here rather than in domain code for two reasons: a handler cannot forget
/// them, and a handler cannot forge them either - nothing in the application layer is trusted to
/// decide which tenant a row belongs to.
/// </para>
/// </summary>
public sealed class TenantAuditSaveChangesInterceptor(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : SaveChangesInterceptor
{
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

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
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
        }
    }

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
