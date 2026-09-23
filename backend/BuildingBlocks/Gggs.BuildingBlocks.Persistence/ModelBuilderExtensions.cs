using System.Linq.Expressions;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gggs.BuildingBlocks.Persistence;

/// <summary>
/// Applies the storage conventions from the architecture to every entity automatically: tenant and
/// soft-delete filters, the clustered (TenantId, Id) key layout, rowversion concurrency and UTC
/// timestamps. Doing it by convention means a new table is isolated and auditable the moment it is
/// added, without anyone having to remember the rules.
/// </summary>
public static class ModelBuilderExtensions
{
    public static ModelBuilder ApplyGggsConventions(this ModelBuilder modelBuilder, GggsDbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.ClrType is null)
            {
                continue;
            }

            Type clrType = entityType.ClrType;

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                ApplyTenantFilter(modelBuilder, context, clrType);
                ApplyTenantKeyLayout(entityType);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                ApplySoftDeleteFilter(modelBuilder, clrType);
            }

            if (typeof(IConcurrencyAware).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IConcurrencyAware.RowVersion))
                    .IsRowVersion();
            }

            ApplyColumnConventions(entityType);
        }

        return modelBuilder;
    }

    /// <summary>
    /// <c>e =&gt; context.IsPlatformScope || e.TenantId == context.CurrentTenantId</c>
    /// <para>
    /// The platform escape is a value inside the expression rather than a different filter, because
    /// changing the shape of a filter per instance would break EF's model cache. Even when it is
    /// true, the database's row-level security still applies unless the connection also uses the
    /// bypass principal.
    /// </para>
    /// </summary>
    private static void ApplyTenantFilter(ModelBuilder modelBuilder, GggsDbContext context, Type clrType)
    {
        ParameterExpression entity = Expression.Parameter(clrType, "e");
        ConstantExpression contextRef = Expression.Constant(context);

        BinaryExpression sameTenant = Expression.Equal(
            Expression.Property(entity, nameof(ITenantOwned.TenantId)),
            Expression.Property(contextRef, nameof(GggsDbContext.CurrentTenantId)));

        BinaryExpression body = Expression.OrElse(
            Expression.Property(contextRef, nameof(GggsDbContext.IsPlatformScope)),
            sameTenant);

        modelBuilder.Entity(clrType).HasQueryFilter(QueryFilters.Tenant, Expression.Lambda(body, entity));
    }

    /// <summary>
    /// Soft-deleted rows stay out of every query unless a caller explicitly asks for them, which
    /// they do by name so that waiving it never accidentally waives tenant isolation too.
    /// </summary>
    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type clrType)
    {
        ParameterExpression entity = Expression.Parameter(clrType, "e");
        UnaryExpression body = Expression.Not(Expression.Property(entity, nameof(ISoftDeletable.IsDeleted)));

        modelBuilder.Entity(clrType).HasQueryFilter(QueryFilters.SoftDelete, Expression.Lambda(body, entity));
    }

    /// <summary>
    /// Primary key non-clustered on Id, with the clustered index on (TenantId, Id) (ADR-08), so a
    /// tenant's rows sit together on disk and tenant-scoped range scans stay cheap.
    /// </summary>
    private static void ApplyTenantKeyLayout(IMutableEntityType entityType)
    {
        IMutableKey? primaryKey = entityType.FindPrimaryKey();
        IMutableProperty? tenantId = entityType.FindProperty(nameof(ITenantOwned.TenantId));
        IMutableProperty? id = entityType.FindProperty(nameof(IEntity.Id));

        if (primaryKey is null || tenantId is null || id is null)
        {
            return;
        }

        primaryKey.SetIsClustered(false);

        bool alreadyDeclared = entityType.GetIndexes().Any(index =>
            index.Properties.Count == 2 &&
            index.Properties[0] == tenantId &&
            index.Properties[1] == id);

        if (!alreadyDeclared)
        {
            IMutableIndex clustered = entityType.AddIndex([tenantId, id]);
            clustered.IsUnique = true;
            clustered.SetIsClustered(true);
            clustered.SetDatabaseName($"UX_{entityType.GetTableName()}_TenantId_Id");
        }

        tenantId.IsNullable = false;
    }

    private static void ApplyColumnConventions(IMutableEntityType entityType)
    {
        foreach (IMutableProperty property in entityType.GetProperties())
        {
            // Timestamps are UTC to the millisecond: datetime2(3) is enough for HR and indexes
            // better than the default datetime2(7).
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
            {
                property.SetColumnType("datetime2(3)");
            }

            // Money defaults that are safe for payroll; components that need more set their own.
            if ((property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                && property.GetColumnType() is null)
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }
        }
    }
}
