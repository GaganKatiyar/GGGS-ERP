using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Platform.Infrastructure;

/// <summary>
/// The platform database: the tenant registry, plans, subscriptions and per-tenant settings.
/// <para>
/// Unusual among the services in that most of its tables are deliberately platform-owned. This is
/// the registry every other database's <c>TenantId</c> points at, so it cannot itself be
/// tenant-filtered - the lookup that resolves a tenant would need a tenant to run.
/// </para>
/// <para>
/// The exception is <see cref="TenantSetting"/>, which is per-tenant configuration and is filtered
/// and RLS-protected like any other tenant-owned table.
/// </para>
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    public const string Schema = "platform";

    protected override string DefaultSchema => Schema;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenant");
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
            entity.ToTable("Plan");
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
            entity.ToTable("Subscription");
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
            entity.ToTable("TenantSetting");
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
            entity.ToTable("TenantDatabase");
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

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
