using Gggs.Data;
using Gggs.Modules.Identity.Domain.ApiKeys;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Sso;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// The Identity module's tables, in the <c>iam</c> schema, plus OpenIddict's own in <c>oidc</c>.
/// <para>
/// Every <c>iam</c> table is tenant-owned, including users: one person working
/// for two customers has two accounts, and the same email address can exist in both. That is
/// deliberate. Sharing one account across tenants would mean a single password, a single lockout and
/// a single compromise reaching both customers.
/// </para>
/// </summary>
internal sealed class IdentityModel : IModuleModel
{
    public const string Schema = "iam";

    /// <summary>
    /// Schema for the OIDC server's own tables.
    /// <para>
    /// Kept apart from <c>iam</c> because these are not our tables: their shape is OpenIddict's, and
    /// a migration of theirs should be visibly separate from a migration of ours.
    /// </para>
    /// </summary>
    public const string OpenIddictSchema = "oidc";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("ApiKey", Schema);
            entity.Property(key => key.Name).HasMaxLength(100).IsRequired();
            entity.Property(key => key.Prefix).HasMaxLength(16).IsRequired();
            entity.Property(key => key.SecretHash).HasMaxLength(64).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(key => key.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(key => new { key.TenantId, key.Prefix })
                .HasDatabaseName("UX_ApiKey_TenantId_Prefix")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(key => key.DomainEvents);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User", Schema);
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

        modelBuilder.Entity<RecentPage>(entity =>
        {
            entity.ToTable("RecentPage", Schema);
            entity.Property(page => page.Route).HasMaxLength(RecentPage.MaxRouteLength).IsRequired();

            // No key to User: these rows are disposable, and nothing should have to clear them first.
            entity.HasIndex(page => new { page.TenantId, page.UserId, page.Route })
                .HasDatabaseName("UX_RecentPage_TenantId_UserId_Route")
                .IsUnique();

            entity.Ignore(page => page.DomainEvents);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role", Schema);
            entity.Property(role => role.AutoAssign).HasConversion<byte>();
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
            entity.ToTable("UserRole", Schema);
            entity.Property(grant => grant.ScopeType).HasConversion<byte?>();
            entity.Property(grant => grant.Reason).HasMaxLength(500);

            entity.HasIndex(grant => new { grant.TenantId, grant.UserId })
                .HasDatabaseName("IX_UserRole_TenantId_UserId");

            entity.HasIndex(grant => new { grant.TenantId, grant.RoleId })
                .HasDatabaseName("IX_UserRole_TenantId_RoleId");

            // The same role twice for the same anchor is a duplicate, not a second grant - among open
            // grants. An ended grant stays as history, so the role can be given again later.
            entity.HasIndex(grant => new { grant.TenantId, grant.UserId, grant.RoleId, grant.ScopeType, grant.ScopeId })
                .HasDatabaseName("UX_UserRole_Assignment")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [ValidTo] IS NULL");
        });

        modelBuilder.Entity<PasswordPolicy>(entity =>
        {
            entity.ToTable("PasswordPolicy", Schema);

            // One policy per tenant.
            entity.HasIndex(policy => policy.TenantId)
                .HasDatabaseName("UX_PasswordPolicy_TenantId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<SsoProvider>(entity =>
        {
            entity.ToTable("SsoProvider", Schema);
            entity.Property(provider => provider.Key).HasMaxLength(40).IsRequired();
            entity.Property(provider => provider.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(provider => provider.Authority).HasMaxLength(300).IsRequired();
            entity.Property(provider => provider.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(provider => provider.ClientSecret).HasMaxLength(2000);
            entity.Property(provider => provider.Scopes).HasMaxLength(300).IsRequired();
            entity.Property(provider => provider.AllowedDomains).HasMaxLength(500);
            entity.Ignore(provider => provider.LoginProvider);
            entity.Ignore(provider => provider.DomainEvents);
            entity.HasIndex(provider => new { provider.TenantId, provider.Key })
                .HasDatabaseName("UX_SsoProvider_Key")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        // The OIDC server's applications, authorizations, scopes and tokens.
        //
        // Deliberately not tenant-owned, for the same reason the outbox is not: the token endpoint
        // runs before any tenant context exists - it is what establishes who the caller is - so a
        // tenant query filter here would make every token lookup return nothing. Isolation comes from
        // the tenant claim inside each token, which is issued from the user record and cannot be
        // asked for.
        modelBuilder.UseOpenIddict();

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes()
            .Where(type => type.ClrType.Namespace?.StartsWith("OpenIddict", StringComparison.Ordinal) == true))
        {
            entityType.SetSchema(OpenIddictSchema);
        }
    }
}

/// <summary>The Identity module's tables, as typed sets on the shared context.</summary>
internal static class IdentityData
{
    extension(AppDbContext db)
    {
        public DbSet<User> Users => db.Set<User>();

        public DbSet<RecentPage> RecentPages => db.Set<RecentPage>();

        public DbSet<Role> Roles => db.Set<Role>();

        public DbSet<UserRole> UserRoles => db.Set<UserRole>();

        public DbSet<PasswordPolicy> PasswordPolicies => db.Set<PasswordPolicy>();

        public DbSet<SsoProvider> SsoProviders => db.Set<SsoProvider>();

        public DbSet<ApiKey> ApiKeys => db.Set<ApiKey>();
    }
}
