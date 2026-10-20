using Gggs.Data;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Sso;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// The Identity module's tables, in the <c>iam</c> schema, plus OpenIddict's own in <c>oidc</c>.
/// <para>
/// Every <c>iam</c> table except platform staff is tenant-owned, including users: one person working
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
            entity.Property(user => user.MfaSecret).HasMaxLength(1000);
            entity.Property(user => user.MfaPendingSecret).HasMaxLength(1000);
            entity.Property(user => user.MfaRecoveryCodes).HasMaxLength(1000);
            entity.Ignore(user => user.MfaRecoveryCodesLeft);

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
            entity.ToTable("Role", Schema);
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

            // The same role twice for the same anchor is a duplicate, not a second grant.
            entity.HasIndex(grant => new { grant.TenantId, grant.UserId, grant.RoleId, grant.ScopeType, grant.ScopeId })
                .HasDatabaseName("UX_UserRole_Assignment")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<PasswordPolicy>(entity =>
        {
            entity.ToTable("PasswordPolicy", Schema);
            entity.Property(policy => policy.MfaRequirement).HasConversion<byte>();

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

        modelBuilder.Entity<PlatformStaffMember>(entity =>
        {
            entity.ToTable("PlatformStaff", Schema);
            entity.Property(member => member.Email).HasMaxLength(256).IsRequired();
            entity.Property(member => member.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(member => member.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(member => member.SecurityStamp).HasMaxLength(64).IsRequired();
            entity.Property(member => member.Status).HasConversion<byte>();

            // Global, not per-tenant: there is exactly one platform, so an address can hold at
            // most one platform staff account, unlike a tenant user's per-tenant uniqueness.
            entity.HasIndex(member => member.NormalizedEmail)
                .HasDatabaseName("UX_PlatformStaff_NormalizedEmail")
                .IsUnique();

            entity.OwnsMany(member => member.PasswordHistory, history =>
            {
                history.ToJson("PasswordHistory");
            });
            entity.Navigation(member => member.PasswordHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(member => member.DomainEvents);
        });

        modelBuilder.Entity<PlatformRole>(entity =>
        {
            entity.ToTable("PlatformRole", Schema);
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
            entity.Property(role => role.NormalizedName).HasMaxLength(100).IsRequired();
            entity.Property(role => role.Description).HasMaxLength(500);
            entity.Property(role => role.TemplateCode).HasMaxLength(50);

            entity.HasIndex(role => role.NormalizedName)
                .HasDatabaseName("UX_PlatformRole_NormalizedName")
                .IsUnique();

            // A JSON array rather than a join table: read as a whole on every permission check,
            // never queried by element, and a table would mean a migration for every role change.
            entity.PrimitiveCollection(role => role.PermissionCodes)
                .HasColumnName("PermissionCodes")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(role => role.DomainEvents);
        });

        modelBuilder.Entity<PlatformRoleAssignment>(entity =>
        {
            entity.ToTable("PlatformRoleAssignment", Schema);

            entity.HasIndex(grant => grant.PlatformStaffId)
                .HasDatabaseName("IX_PlatformRoleAssignment_PlatformStaffId");

            entity.HasIndex(grant => grant.PlatformRoleId)
                .HasDatabaseName("IX_PlatformRoleAssignment_PlatformRoleId");

            // The same role granted to the same person twice is a duplicate, not a second grant -
            // there is no anchor or date range here to tell two such rows apart.
            entity.HasIndex(grant => new { grant.PlatformStaffId, grant.PlatformRoleId })
                .HasDatabaseName("UX_PlatformRoleAssignment_Assignment")
                .IsUnique();

            entity.Ignore(grant => grant.DomainEvents);
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

        public DbSet<Role> Roles => db.Set<Role>();

        public DbSet<UserRole> UserRoles => db.Set<UserRole>();

        public DbSet<PasswordPolicy> PasswordPolicies => db.Set<PasswordPolicy>();

        public DbSet<SsoProvider> SsoProviders => db.Set<SsoProvider>();

        /// <summary>
        /// Our own staff. Deliberately not tenant-owned; see <see cref="PlatformStaffMember"/> for why
        /// it is a separate aggregate rather than a <see cref="User"/> with no tenant.
        /// </summary>
        public DbSet<PlatformStaffMember> PlatformStaff => db.Set<PlatformStaffMember>();

        public DbSet<PlatformRole> PlatformRoles => db.Set<PlatformRole>();

        public DbSet<PlatformRoleAssignment> PlatformRoleAssignments => db.Set<PlatformRoleAssignment>();
    }
}
