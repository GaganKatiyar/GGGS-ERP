using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Reporting.Domain.Headcount;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Reporting.Infrastructure;

/// <summary>Reporting's database: the headcount roster fed from Core HR's events, and the daily
/// snapshot facts built from it.</summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "rpt";

    public DbSet<EmployeeRosterFact> EmployeeRosterFacts => Set<EmployeeRosterFact>();

    public DbSet<HeadcountDailySnapshot> HeadcountDailySnapshots => Set<HeadcountDailySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<EmployeeRosterFact>(entity =>
        {
            entity.ToTable("EmployeeRosterFact", "rpt");
            entity.Property(x => x.EmployeeCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("UX_EmployeeRosterFact_TenantId_EmployeeId")
                .IsUnique();

            entity.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.BranchId, x.DepartmentId })
                .HasDatabaseName("IX_EmployeeRosterFact_TenantId_OrgUnit");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<HeadcountDailySnapshot>(entity =>
        {
            entity.ToTable("HeadcountDailySnapshot", "rpt");

            entity.HasIndex(x => new { x.TenantId, x.SnapshotDate, x.LegalEntityId, x.BranchId, x.DepartmentId })
                .HasDatabaseName("UX_HeadcountDailySnapshot_TenantId_Date_OrgUnit")
                .IsUnique();

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
