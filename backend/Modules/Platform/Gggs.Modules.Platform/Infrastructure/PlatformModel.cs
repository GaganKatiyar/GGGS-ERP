using Gggs.Data;
using Gggs.Modules.Platform.Domain.Audit;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Infrastructure;

/// <summary>
/// The Platform module's tables: the tenant registry, plans, subscriptions, per-tenant settings and
/// the audit log, all in the <c>platform</c> schema.
/// <para>
/// Unusual among the modules in that most of these tables are deliberately platform-owned. This is
/// the registry every other table's <c>TenantId</c> points at, so it cannot itself be
/// tenant-filtered - the lookup that resolves a tenant would need a tenant to run.
/// </para>
/// <para>
/// The exceptions are <see cref="TenantSetting"/> and <see cref="AuditLog"/>, which are per-tenant
/// and filtered and RLS-protected like any other tenant-owned table.
/// </para>
/// </summary>
internal sealed class PlatformModel : IModuleModel
{
    public const string Schema = "platform";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenant", Schema);
            entity.Property(tenant => tenant.Code).HasMaxLength(32).IsRequired();
            entity.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
            entity.Property(tenant => tenant.LegalName).HasMaxLength(300);
            entity.Property(tenant => tenant.Status).HasConversion<byte>();
            entity.Property(tenant => tenant.IsolationTier).HasConversion<byte>();
            entity.Property(tenant => tenant.DataRegion).HasMaxLength(32).IsRequired();
            entity.Property(tenant => tenant.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
            entity.Property(tenant => tenant.DefaultTimeZone).HasMaxLength(64).IsRequired();
            entity.Property(tenant => tenant.DefaultCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(tenant => tenant.DefaultLocale).HasMaxLength(10).IsRequired();
            entity.Property(tenant => tenant.SuspensionReason).HasMaxLength(500);

            // Not filtered on a deleted flag, unlike every other unique index in the platform: a
            // purged tenant keeps its code forever, so it can never be handed to another customer
            // and let old links resolve to them.
            entity.HasIndex(tenant => tenant.Code)
                .HasDatabaseName("UX_Tenant_Code")
                .IsUnique();

            entity.HasIndex(tenant => tenant.Status).HasDatabaseName("IX_Tenant_Status");
            entity.Ignore(tenant => tenant.DomainEvents);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.ToTable("Plan", Schema);
            entity.Property(plan => plan.Code).HasMaxLength(32).IsRequired();
            entity.Property(plan => plan.Name).HasMaxLength(100).IsRequired();
            entity.Property(plan => plan.Edition).HasConversion<byte>();
            entity.HasIndex(plan => plan.Code).HasDatabaseName("UX_Plan_Code").IsUnique();

            // Stored as a JSON array: the feature list is read as a whole, never queried by
            // element, and a join table would mean a migration for every plan change.
            entity.PrimitiveCollection(plan => plan.FeatureCodes)
                .HasColumnName("FeatureCodes")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(plan => plan.DomainEvents);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscription", Schema);
            entity.Property(subscription => subscription.Status).HasConversion<byte>();
            entity.Property(subscription => subscription.BillingReference).HasMaxLength(100);

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(subscription => subscription.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Plan>()
                .WithMany()
                .HasForeignKey(subscription => subscription.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            // One live subscription per tenant; cancelled and expired ones stay for billing history.
            entity.HasIndex(subscription => subscription.TenantId)
                .HasDatabaseName("UX_Subscription_TenantId_Live")
                .IsUnique()
                .HasFilter("[Status] IN (1, 2, 3)");

            // Owned JSON rather than a table: overrides are always loaded with the subscription
            // and never queried on their own.
            entity.OwnsMany(subscription => subscription.FeatureOverrides, overrides =>
            {
                overrides.ToJson("FeatureOverrides");
            });
            entity.Navigation(subscription => subscription.FeatureOverrides)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(subscription => subscription.DomainEvents);
        });

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

        modelBuilder.Entity<TenantDatabase>(entity =>
        {
            entity.ToTable("TenantDatabase", Schema);
            entity.Property(database => database.ServiceName).HasMaxLength(50).IsRequired();
            entity.Property(database => database.ConnectionSecretName).HasMaxLength(200).IsRequired();
            entity.Property(database => database.ShardName).HasMaxLength(100).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(database => database.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(database => new { database.TenantId, database.ServiceName })
                .HasDatabaseName("UX_TenantDatabase_TenantId_ServiceName")
                .IsUnique();
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
        public DbSet<Tenant> Tenants => db.Set<Tenant>();

        public DbSet<Plan> Plans => db.Set<Plan>();

        public DbSet<Subscription> Subscriptions => db.Set<Subscription>();

        public DbSet<TenantSetting> TenantSettings => db.Set<TenantSetting>();

        public DbSet<TenantDatabase> TenantDatabases => db.Set<TenantDatabase>();

        public DbSet<AuditLog> AuditLogs => db.Set<AuditLog>();
    }
}
