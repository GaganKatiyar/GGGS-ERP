using Gggs.Data;
using Gggs.Modules.Platform.Domain.Audit;
using Gggs.Modules.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Infrastructure;

/// <summary>
/// The Platform module's tables in the HR database: each organisation's own settings and audit log,
/// in the <c>platform</c> schema. Both are tenant-owned, filtered and row-level-secured.
/// <para>
/// Organisations, plans and subscriptions are not here: they live in the platform console's database
/// (NaBPlatform), which the HR application reads through <see cref="PlatformCatalogDbContext"/>.
/// </para>
/// </summary>
internal sealed class PlatformModel : IModuleModel
{
    public const string Schema = "platform";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<TenantSetting>(entity =>
        {
            entity.ToTable("TenantSetting", Schema);
            entity.Property(setting => setting.Module).HasMaxLength(50).IsRequired();
            entity.Property(setting => setting.Key).HasMaxLength(100).IsRequired();
            entity.Property(setting => setting.ValueJson).IsRequired();

            entity.HasIndex(setting => new { setting.TenantId, setting.Module, setting.Key })
                .HasDatabaseName("UX_TenantSetting_TenantId_Module_Key")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog", Schema, table => table.HasComment(
                "Append-only. Fed exclusively by AuditEntryConsumer; nothing else writes here."));
            entity.Property(log => log.Service).HasMaxLength(50).IsRequired();
            entity.Property(log => log.Action).HasMaxLength(30).IsRequired();
            entity.Property(log => log.EntityType).HasMaxLength(200).IsRequired();
            entity.Property(log => log.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(log => log.CorrelationId).HasMaxLength(100);

            // The log viewer's two access paths: "everything about this record" and "everything
            // in this window", both always scoped to one tenant first.
            entity.HasIndex(log => new { log.TenantId, log.EntityType, log.EntityId, log.OccurredDate })
                .HasDatabaseName("IX_AuditLog_TenantId_EntityType_EntityId_OccurredDate");
            entity.HasIndex(log => new { log.TenantId, log.OccurredDate })
                .HasDatabaseName("IX_AuditLog_TenantId_OccurredDate");

            entity.Ignore(log => log.DomainEvents);
        });
    }
}

/// <summary>The Platform module's tables, as typed sets on the shared context.</summary>
internal static class PlatformData
{
    extension(AppDbContext db)
    {
        public DbSet<TenantSetting> TenantSettings => db.Set<TenantSetting>();

        public DbSet<AuditLog> AuditLogs => db.Set<AuditLog>();
    }
}
