using Gggs.Data;
using Gggs.Modules.Compensation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compensation.Infrastructure;

/// <summary>The Compensation module's tables, in the <c>comp</c> schema.</summary>
internal sealed class CompensationModel : IModuleModel
{
    public const string Schema = "comp";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SalaryBand>(entity =>
        {
            entity.ToTable("SalaryBand", Schema);
            entity.Property(band => band.Currency).HasMaxLength(3);
            entity.Property(band => band.Minimum).HasPrecision(18, 2);
            entity.Property(band => band.Midpoint).HasPrecision(18, 2);
            entity.Property(band => band.Maximum).HasPrecision(18, 2);
            entity.Property(band => band.Notes).HasMaxLength(500);
            entity.HasIndex(band => new { band.TenantId, band.GradeId, band.Currency, band.EffectiveFrom })
                .HasDatabaseName("UX_SalaryBand_TenantId_Grade_Currency_From")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(band => band.DomainEvents);
        });

        modelBuilder.Entity<CompensationCycle>(entity =>
        {
            entity.ToTable("CompensationCycle", Schema);
            entity.Property(cycle => cycle.Name).HasMaxLength(200);
            entity.Property(cycle => cycle.Status).HasConversion<byte>();
            entity.Property(cycle => cycle.BudgetPercent).HasPrecision(5, 2);
            entity.Property(cycle => cycle.Notes).HasMaxLength(1000);
            entity.Ignore(cycle => cycle.DomainEvents);
        });

        modelBuilder.Entity<CompensationRevision>(entity =>
        {
            entity.ToTable("CompensationRevision", Schema);
            entity.Property(revision => revision.Type).HasConversion<byte>();
            entity.Property(revision => revision.Status).HasConversion<byte>();
            entity.Property(revision => revision.CurrentCtc).HasPrecision(18, 2);
            entity.Property(revision => revision.ProposedCtc).HasPrecision(18, 2);
            entity.Property(revision => revision.Currency).HasMaxLength(3);
            entity.Property(revision => revision.NewDesignation).HasMaxLength(200);
            entity.Property(revision => revision.Justification).HasMaxLength(2000);
            entity.Property(revision => revision.PerformanceRating).HasMaxLength(50);
            entity.Property(revision => revision.ApplyError).HasMaxLength(500);
            entity.HasOne<CompensationCycle>().WithMany().HasForeignKey(revision => revision.CycleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(revision => new { revision.TenantId, revision.EmployeeId, revision.EffectiveDate }).HasDatabaseName("IX_CompensationRevision_TenantId_Employee_Date");
            entity.HasIndex(revision => new { revision.TenantId, revision.CycleId }).HasDatabaseName("IX_CompensationRevision_TenantId_Cycle");
            entity.Ignore(revision => revision.IncreasePercent);
            entity.Ignore(revision => revision.DomainEvents);
        });

        modelBuilder.Entity<VariablePayAward>(entity =>
        {
            entity.ToTable("VariablePayAward", Schema);
            entity.Property(award => award.Type).HasConversion<byte>();
            entity.Property(award => award.Status).HasConversion<byte>();
            entity.Property(award => award.Amount).HasPrecision(18, 2);
            entity.Property(award => award.Currency).HasMaxLength(3);
            entity.Property(award => award.Reason).HasMaxLength(500);
            entity.HasIndex(award => new { award.TenantId, award.EmployeeId, award.PayoutMonth }).HasDatabaseName("IX_VariablePayAward_TenantId_Employee_Month");
            entity.Ignore(award => award.DomainEvents);
        });

        modelBuilder.Entity<EquityGrant>(entity =>
        {
            entity.ToTable("EquityGrant", Schema);
            entity.Property(grant => grant.PlanName).HasMaxLength(200);
            entity.Property(grant => grant.Status).HasConversion<byte>();
            entity.Property(grant => grant.Units).HasPrecision(18, 4);
            entity.Property(grant => grant.ExercisedUnits).HasPrecision(18, 4);
            entity.Property(grant => grant.StrikePrice).HasPrecision(18, 4);
            entity.Property(grant => grant.FairValuePerUnit).HasPrecision(18, 4);
            entity.Property(grant => grant.Currency).HasMaxLength(3);
            entity.Property(grant => grant.Notes).HasMaxLength(1000);
            entity.HasIndex(grant => new { grant.TenantId, grant.EmployeeId }).HasDatabaseName("IX_EquityGrant_TenantId_Employee");
            entity.Ignore(grant => grant.DomainEvents);
        });
    }
}

internal static class CompensationData
{
    extension(AppDbContext db)
    {
        public DbSet<SalaryBand> SalaryBands => db.Set<SalaryBand>();

        public DbSet<CompensationCycle> CompensationCycles => db.Set<CompensationCycle>();

        public DbSet<CompensationRevision> CompensationRevisions => db.Set<CompensationRevision>();

        public DbSet<VariablePayAward> VariablePayAwards => db.Set<VariablePayAward>();

        public DbSet<EquityGrant> EquityGrants => db.Set<EquityGrant>();
    }
}
