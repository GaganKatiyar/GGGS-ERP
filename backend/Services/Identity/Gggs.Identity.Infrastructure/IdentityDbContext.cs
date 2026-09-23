using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Identity.Infrastructure;

/// <summary>
/// The identity database.
/// <para>
/// Every table here is tenant-owned, including users: one person working for two customers has two
/// accounts, and the same email address can exist in both. That is deliberate. Sharing one account
/// across tenants would mean a single password, a single lockout and a single compromise reaching
/// both customers.
/// </para>
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    public const string Schema = "iam";

    protected override string DefaultSchema => Schema;

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<PasswordPolicy> PasswordPolicies => Set<PasswordPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.SecurityStamp).HasMaxLength(64).IsRequired();
            entity.Property(user => user.UserType).HasConversion<byte>();
            entity.Property(user => user.Status).HasConversion<byte>();
            entity.Property(user => user.PreferredLocale).HasMaxLength(10);
            entity.Property(user => user.TimeZone).HasMaxLength(64);

            // Unique per tenant, not globally: the same person may work for two customers.
            entity.HasIndex(user => new { user.TenantId, user.NormalizedEmail })
                .HasDatabaseName("UX_User_TenantId_NormalizedEmail")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // One account per employee, so "who is this person's login" has a single answer.
            entity.HasIndex(user => new { user.TenantId, user.EmployeeId })
                .HasDatabaseName("UX_User_TenantId_EmployeeId")
                .IsUnique()
                .HasFilter("[EmployeeId] IS NOT NULL AND [IsDeleted] = 0");

            entity.OwnsMany(user => user.ExternalLogins, logins =>
            {
                logins.ToJson("ExternalLogins");
            });
            entity.Navigation(user => user.ExternalLogins).UsePropertyAccessMode(PropertyAccessMode.Field);

            // History as JSON rather than a table: it is read only when validating a new password,
            // always for one user, and never queried across users.
            entity.OwnsMany(user => user.PasswordHistory, history =>
            {
                history.ToJson("PasswordHistory");
            });
            entity.Navigation(user => user.PasswordHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(user => user.DomainEvents);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role");
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
            entity.Property(role => role.NormalizedName).HasMaxLength(100).IsRequired();
            entity.Property(role => role.Description).HasMaxLength(500);
            entity.Property(role => role.TemplateCode).HasMaxLength(50);

            entity.HasIndex(role => new { role.TenantId, role.NormalizedName })
                .HasDatabaseName("UX_Role_TenantId_NormalizedName")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.OwnsMany(role => role.Permissions, permissions =>
            {
                permissions.ToJson("Permissions");
            });
            entity.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(role => role.DomainEvents);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRole");
            entity.Property(grant => grant.ScopeType).HasConversion<byte?>();
            entity.Property(grant => grant.Reason).HasMaxLength(500);

            entity.HasIndex(grant => new { grant.TenantId, grant.UserId })
                .HasDatabaseName("IX_UserRole_TenantId_UserId");

            entity.HasIndex(grant => new { grant.TenantId, grant.RoleId })
                .HasDatabaseName("IX_UserRole_TenantId_RoleId");

            // The same role twice for the same anchor is a duplicate, not a second grant.
            entity.HasIndex(grant => new { grant.TenantId, grant.UserId, grant.RoleId, grant.ScopeType, grant.ScopeId })
                .HasDatabaseName("UX_UserRole_Assignment")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<PasswordPolicy>(entity =>
        {
            entity.ToTable("PasswordPolicy");
            entity.Property(policy => policy.MfaRequirement).HasConversion<byte>();

            // One policy per tenant.
            entity.HasIndex(policy => policy.TenantId)
                .HasDatabaseName("UX_PasswordPolicy_TenantId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
