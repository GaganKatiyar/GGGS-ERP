using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

/// <summary>Talent's database: job requisitions (v1's only aggregate) and the employee snapshot fed
/// from Core HR.</summary>
public sealed class TalentDbContext(DbContextOptions<TalentDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "rec";

    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();

    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<JobRequisition>(entity =>
        {
            entity.ToTable("JobRequisition");
            entity.Property(x => x.RequisitionNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Justification).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.RequisitionNo })
                .HasDatabaseName("UX_JobRequisition_TenantId_RequisitionNo")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.Status })
                .HasDatabaseName("IX_JobRequisition_TenantId_Status");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSnapshot>(entity =>
        {
            entity.ToTable("EmployeeSnapshot", "ref");
            entity.Property(x => x.EmployeeCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("UX_EmployeeSnapshot_TenantId_EmployeeId")
                .IsUnique();

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
