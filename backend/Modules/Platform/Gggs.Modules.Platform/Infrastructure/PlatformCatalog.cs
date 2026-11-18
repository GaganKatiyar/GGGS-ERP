using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gggs.Modules.Platform.Infrastructure;

/// <summary>
/// The platform console's database, NaBPlatform, as the HR application sees it.
/// <para>
/// The platform console (repository GGGS-ERP-PLATFORM) owns this database and its schema; the HR app
/// reads organisations, plans and subscriptions from it, carries out the console's requests and
/// reports usage and job health back into the <c>hr</c> schema. Nothing here has migrations in this
/// repository: the shape is the contract in docs/nabplatform-contract.md, kept identical in both repos.
/// </para>
/// </summary>
public sealed class PlatformCatalogDbContext(DbContextOptions<PlatformCatalogDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "PlatformDb";

    private const string Schema = "platform";

    private const string HrSchema = "hr";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<ProvisioningRequestRow> ProvisioningRequests => Set<ProvisioningRequestRow>();

    public DbSet<TenantSeedStateRow> TenantSeedStates => Set<TenantSeedStateRow>();

    public DbSet<TenantUsageRow> TenantUsages => Set<TenantUsageRow>();

    public DbSet<TenantAdministratorRow> TenantAdministrators => Set<TenantAdministratorRow>();

    public DbSet<JobHealthRow> JobHealth => Set<JobHealthRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenant", Schema);
            entity.Property(tenant => tenant.Code).HasMaxLength(32);
            entity.Property(tenant => tenant.Name).HasMaxLength(200);
            entity.Property(tenant => tenant.Status).HasConversion<byte>();
            entity.Property(tenant => tenant.IsolationTier).HasConversion<byte>();
            entity.Property(tenant => tenant.CountryCode).HasMaxLength(2).IsFixedLength();
            entity.Property(tenant => tenant.DefaultCurrency).HasMaxLength(3).IsFixedLength();
            entity.Ignore(tenant => tenant.DomainEvents);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.ToTable("Plan", Schema);
            entity.Property(plan => plan.Edition).HasConversion<byte>();
            entity.PrimitiveCollection(plan => plan.FeatureCodes).HasColumnName("FeatureCodes").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(plan => plan.DomainEvents);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscription", Schema);
            entity.Property(subscription => subscription.Status).HasConversion<byte>();

            // Declared so a new organisation and its subscription are inserted in the right order.
            entity.HasOne<Tenant>().WithMany().HasForeignKey(subscription => subscription.TenantId);
            entity.HasOne<Plan>().WithMany().HasForeignKey(subscription => subscription.PlanId);
            entity.OwnsMany(subscription => subscription.FeatureOverrides, overrides => overrides.ToJson("FeatureOverrides"));
            entity.Navigation(subscription => subscription.FeatureOverrides).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(subscription => subscription.DomainEvents);
        });

        modelBuilder.Entity<ProvisioningRequestRow>(entity =>
        {
            entity.ToTable("ProvisioningRequest", Schema);
            entity.Property(request => request.Kind).HasConversion<byte>();
            entity.Property(request => request.Status).HasConversion<byte>();
        });

        modelBuilder.Entity<TenantSeedStateRow>(entity =>
        {
            entity.ToTable("TenantSeedState", HrSchema);
            entity.HasKey(state => state.TenantId);
        });

        modelBuilder.Entity<TenantUsageRow>(entity => entity.ToTable("TenantUsage", HrSchema));

        modelBuilder.Entity<TenantAdministratorRow>(entity =>
        {
            entity.ToTable("TenantAdministrator", HrSchema);
            entity.HasKey(admin => new { admin.TenantId, admin.UserId });
        });

        modelBuilder.Entity<JobHealthRow>(entity =>
        {
            entity.ToTable("JobHealth", HrSchema);
            entity.Property(health => health.Id).ValueGeneratedNever();
        });

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties()
                .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)))
            {
                property.SetColumnType("datetime2(3)");
            }

            if (!entityType.IsOwned() && entityType.FindProperty("RowVersion") is { } rowVersion)
            {
                rowVersion.IsConcurrencyToken = true;
                rowVersion.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            }
        }
    }
}

public enum ProvisioningRequestKind
{
    AddAdministrator = 1,
    ResendAdministratorInvitation = 2,
    RetryJob = 3,
    SeedOrganisation = 4,
}

public enum ProvisioningRequestStatus
{
    Pending = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>Something platform staff asked the HR application to do. The HR worker carries it out.</summary>
public sealed class ProvisioningRequestRow
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public ProvisioningRequestKind Kind { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public string? JobId { get; set; }

    public ProvisioningRequestStatus Status { get; set; }

    public string? Result { get; set; }

    public DateTime RequestedDate { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime? CompletedDate { get; set; }

    public void Complete(bool succeeded, string result, DateTime now)
    {
        Status = succeeded ? ProvisioningRequestStatus.Done : ProvisioningRequestStatus.Failed;
        Result = result.Length > 500 ? result[..500] : result;
        CompletedDate = now;
    }
}

public sealed class TenantSeedStateRow
{
    public Guid TenantId { get; set; }

    public DateTime SeededDate { get; set; }
}

public sealed class TenantUsageRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public DateTime MeasuredDate { get; set; }

    public int Employees { get; set; }

    public int Users { get; set; }

    public int SignedInToday { get; set; }

    public DateTime? LastSignIn { get; set; }

    public int EmailsSentToday { get; set; }

    public int EmailsFailedToday { get; set; }

    public int EmailsLoggedToday { get; set; }

    public string? EmailChannel { get; set; }

    public string? SignIn { get; set; }
}

public sealed class TenantAdministratorRow
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime? LastLoginDate { get; set; }

    public DateTime? InvitationExpiresDate { get; set; }

    public DateTime MeasuredDate { get; set; }
}

public sealed class JobHealthRow
{
    public int Id { get; set; }

    public DateTime MeasuredDate { get; set; }

    public string ServersJson { get; set; } = "[]";

    public string RecurringJson { get; set; } = "[]";

    public string FailedJson { get; set; } = "[]";
}
