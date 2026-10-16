using Gggs.Data;
using Gggs.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compliance.Infrastructure;

/// <summary>The Compliance module's tables, in the <c>cmp</c> schema.</summary>
internal sealed class ComplianceModel : IModuleModel
{
    public const string Schema = "cmp";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<StatutoryRule>(entity =>
        {
            entity.ToTable("StatutoryRule", Schema);
            entity.Property(rule => rule.Code).HasMaxLength(40);
            entity.Property(rule => rule.Name).HasMaxLength(200);
            entity.Property(rule => rule.Type).HasConversion<byte>();
            entity.Property(rule => rule.Status).HasConversion<byte>();
            entity.Property(rule => rule.CountryCode).HasMaxLength(8);
            entity.Property(rule => rule.StateCode).HasMaxLength(16);
            entity.Property(rule => rule.Configuration).IsRequired();
            entity.Property(rule => rule.Eligibility).HasMaxLength(1000);
            entity.Property(rule => rule.Notes).HasMaxLength(2000);
            entity.HasIndex(rule => new { rule.TenantId, rule.Code, rule.Version })
                .HasDatabaseName("UX_StatutoryRule_TenantId_Code_Version")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(rule => new { rule.TenantId, rule.Type, rule.StateCode, rule.EffectiveFrom }).HasDatabaseName("IX_StatutoryRule_TenantId_Type_State");
            entity.Ignore(rule => rule.DomainEvents);
        });

        modelBuilder.Entity<ComplianceObligation>(entity =>
        {
            entity.ToTable("ComplianceObligation", Schema);
            entity.Property(obligation => obligation.Code).HasMaxLength(40);
            entity.Property(obligation => obligation.Name).HasMaxLength(200);
            entity.Property(obligation => obligation.Kind).HasConversion<byte>();
            entity.Property(obligation => obligation.Frequency).HasConversion<byte>();
            entity.Property(obligation => obligation.StateCode).HasMaxLength(16);
            entity.Property(obligation => obligation.Description).HasMaxLength(1000);
            entity.HasIndex(obligation => new { obligation.TenantId, obligation.Code })
                .HasDatabaseName("UX_ComplianceObligation_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(obligation => obligation.DomainEvents);
        });

        modelBuilder.Entity<ComplianceFiling>(entity =>
        {
            entity.ToTable("ComplianceFiling", Schema);
            entity.Property(filing => filing.Name).HasMaxLength(200);
            entity.Property(filing => filing.Kind).HasConversion<byte>();
            entity.Property(filing => filing.Status).HasConversion<byte>();
            entity.Property(filing => filing.PeriodLabel).HasMaxLength(40);
            entity.Property(filing => filing.Reference).HasMaxLength(100);
            entity.Property(filing => filing.Amount).HasPrecision(18, 2);
            entity.Property(filing => filing.Notes).HasMaxLength(1000);
            entity.HasOne<ComplianceObligation>().WithMany().HasForeignKey(filing => filing.ObligationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(filing => new { filing.TenantId, filing.ObligationId, filing.PeriodStart })
                .HasDatabaseName("UX_ComplianceFiling_TenantId_Obligation_Period")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(filing => new { filing.TenantId, filing.Status, filing.DueDate }).HasDatabaseName("IX_ComplianceFiling_TenantId_Status_Due");
            entity.Ignore(filing => filing.DomainEvents);
        });

        modelBuilder.Entity<ComplianceRegistration>(entity =>
        {
            entity.ToTable("ComplianceRegistration", Schema);
            entity.Property(registration => registration.Kind).HasConversion<byte>();
            entity.Property(registration => registration.Name).HasMaxLength(200);
            entity.Property(registration => registration.Number).HasMaxLength(100);
            entity.Property(registration => registration.Authority).HasMaxLength(200);
            entity.Property(registration => registration.StateCode).HasMaxLength(16);
            entity.Property(registration => registration.Notes).HasMaxLength(2000);
            entity.HasIndex(registration => new { registration.TenantId, registration.ExpiresOn }).HasDatabaseName("IX_ComplianceRegistration_TenantId_Expires");
            entity.Ignore(registration => registration.DomainEvents);
        });
    }
}

internal static class ComplianceData
{
    extension(AppDbContext db)
    {
        public DbSet<StatutoryRule> StatutoryRules => db.Set<StatutoryRule>();

        public DbSet<ComplianceObligation> ComplianceObligations => db.Set<ComplianceObligation>();

        public DbSet<ComplianceFiling> ComplianceFilings => db.Set<ComplianceFiling>();

        public DbSet<ComplianceRegistration> ComplianceRegistrations => db.Set<ComplianceRegistration>();
    }
}
