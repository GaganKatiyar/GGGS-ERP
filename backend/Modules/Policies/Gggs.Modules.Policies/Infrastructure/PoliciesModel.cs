using Gggs.Data;
using Gggs.Modules.Policies.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Policies.Infrastructure;

/// <summary>The Policies module's tables, in the <c>pol</c> schema.</summary>
internal sealed class PoliciesModel : IModuleModel
{
    public const string Schema = "pol";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Policy>(entity =>
        {
            entity.ToTable("Policy", Schema);
            entity.Property(policy => policy.Code).HasMaxLength(32).IsRequired();
            entity.Property(policy => policy.Title).HasMaxLength(200).IsRequired();
            entity.Property(policy => policy.Summary).HasMaxLength(1000);
            entity.Property(policy => policy.Category).HasConversion<byte>();
            entity.Property(policy => policy.AudienceDepartmentIdsJson).IsRequired();
            entity.Property(policy => policy.AudienceLocationIdsJson).IsRequired();
            entity.HasIndex(policy => new { policy.TenantId, policy.Code })
                .HasDatabaseName("UX_Policy_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(policy => policy.AudienceDepartmentIds);
            entity.Ignore(policy => policy.AudienceLocationIds);
            entity.Ignore(policy => policy.AppliesToEveryone);
            entity.Ignore(policy => policy.DomainEvents);
        });

        modelBuilder.Entity<PolicyVersion>(entity =>
        {
            entity.ToTable("PolicyVersion", Schema);
            entity.Property(version => version.Content).IsRequired();
            entity.Property(version => version.ChangeSummary).HasMaxLength(500);
            entity.Property(version => version.Status).HasConversion<byte>();
            entity.HasOne<Policy>().WithMany().HasForeignKey(version => version.PolicyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(version => new { version.TenantId, version.PolicyId, version.Number })
                .HasDatabaseName("UX_PolicyVersion_TenantId_Policy_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // At most one version being worked on (draft or awaiting approval). "One in effect" is kept
            // by the publisher, which supersedes the old version in the same save: a filtered unique
            // index on Published would trip mid-save, since the two updates' order is not guaranteed.
            entity.HasIndex(version => new { version.TenantId, version.PolicyId })
                .HasDatabaseName("UX_PolicyVersion_TenantId_Policy_Open")
                .IsUnique()
                .HasFilter("[Status] IN (1, 2) AND [IsDeleted] = 0");
            entity.HasIndex(version => new { version.TenantId, version.Status, version.PolicyId })
                .HasDatabaseName("IX_PolicyVersion_TenantId_Status");
            entity.Ignore(version => version.DomainEvents);
        });

        modelBuilder.Entity<PolicyAcknowledgement>(entity =>
        {
            entity.ToTable("PolicyAcknowledgement", Schema);
            entity.Property(acknowledgement => acknowledgement.IpAddress).HasMaxLength(64);
            entity.HasOne<PolicyVersion>().WithMany().HasForeignKey(acknowledgement => acknowledgement.PolicyVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(acknowledgement => new { acknowledgement.TenantId, acknowledgement.PolicyVersionId, acknowledgement.EmployeeId })
                .HasDatabaseName("UX_PolicyAcknowledgement_TenantId_Version_Employee")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(acknowledgement => new { acknowledgement.TenantId, acknowledgement.EmployeeId })
                .HasDatabaseName("IX_PolicyAcknowledgement_TenantId_Employee");
            entity.Ignore(acknowledgement => acknowledgement.DomainEvents);
        });
    }
}

internal static class PoliciesData
{
    extension(AppDbContext db)
    {
        public DbSet<Policy> Policies => db.Set<Policy>();

        public DbSet<PolicyVersion> PolicyVersions => db.Set<PolicyVersion>();

        public DbSet<PolicyAcknowledgement> PolicyAcknowledgements => db.Set<PolicyAcknowledgement>();
    }
}
