using Gggs.Data;
using Gggs.Modules.Resources.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Resources.Infrastructure;

/// <summary>The Resources module's tables, in the <c>res</c> schema.</summary>
internal sealed class ResourcesModel : IModuleModel
{
    public const string Schema = "res";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ResourceProfile>(entity =>
        {
            entity.ToTable("ResourceProfile", Schema);
            entity.Property(profile => profile.CostRate).HasPrecision(18, 2);
            entity.Property(profile => profile.BillingRate).HasPrecision(18, 2);
            entity.Property(profile => profile.CapacityHoursPerWeek).HasPrecision(5, 2);
            entity.Property(profile => profile.Currency).HasMaxLength(8).IsRequired();
            entity.Property(profile => profile.Notes).HasMaxLength(1000);
            entity.HasIndex(profile => new { profile.TenantId, profile.EmployeeId })
                .HasDatabaseName("UX_ResourceProfile_TenantId_Employee")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(profile => profile.HoursPerDay);
            entity.Ignore(profile => profile.DomainEvents);
        });

        modelBuilder.Entity<ResourceRequest>(entity =>
        {
            entity.ToTable("ResourceRequest", Schema);
            entity.Property(request => request.Title).HasMaxLength(200).IsRequired();
            entity.Property(request => request.SkillsJson).IsRequired();
            entity.Property(request => request.MinYearsExperience).HasPrecision(4, 1);
            entity.Property(request => request.MaxCostRate).HasPrecision(18, 2);
            entity.Property(request => request.WorkModeCode).HasMaxLength(64);
            entity.Property(request => request.Notes).HasMaxLength(2000);
            entity.Property(request => request.Priority).HasConversion<byte>();
            entity.Property(request => request.Status).HasConversion<byte>();
            entity.HasIndex(request => new { request.TenantId, request.Status }).HasDatabaseName("IX_ResourceRequest_TenantId_Status");
            entity.HasIndex(request => new { request.TenantId, request.ProjectId }).HasDatabaseName("IX_ResourceRequest_TenantId_Project");
            entity.Ignore(request => request.Skills);
            entity.Ignore(request => request.Remaining);
            entity.Ignore(request => request.IsOpen);
            entity.Ignore(request => request.DomainEvents);
        });

        modelBuilder.Entity<ResourceCandidate>(entity =>
        {
            entity.ToTable("ResourceCandidate", Schema);
            entity.Property(candidate => candidate.Score).HasPrecision(5, 1);
            entity.Property(candidate => candidate.BreakdownJson).IsRequired();
            entity.Property(candidate => candidate.Note).HasMaxLength(500);
            entity.Property(candidate => candidate.Status).HasConversion<byte>();
            entity.HasOne<ResourceRequest>().WithMany().HasForeignKey(candidate => candidate.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(candidate => new { candidate.TenantId, candidate.RequestId, candidate.EmployeeId })
                .HasDatabaseName("UX_ResourceCandidate_TenantId_Request_Employee")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(candidate => candidate.Breakdown);
            entity.Ignore(candidate => candidate.DomainEvents);
        });

        modelBuilder.Entity<ResourceTransfer>(entity =>
        {
            entity.ToTable("ResourceTransfer", Schema);
            entity.Property(transfer => transfer.Reason).HasMaxLength(500).IsRequired();
            entity.Property(transfer => transfer.Status).HasConversion<byte>();
            entity.HasIndex(transfer => new { transfer.TenantId, transfer.EmployeeId }).HasDatabaseName("IX_ResourceTransfer_TenantId_Employee");
            entity.HasIndex(transfer => new { transfer.TenantId, transfer.FromMemberId }, "UX_ResourceTransfer_TenantId_FromMember_Pending")
                .IsUnique()
                .HasFilter("[Status] = 1 AND [IsDeleted] = 0");
            entity.Ignore(transfer => transfer.DomainEvents);
        });
    }
}

internal static class ResourcesData
{
    extension(AppDbContext db)
    {
        public DbSet<ResourceProfile> ResourceProfiles => db.Set<ResourceProfile>();

        public DbSet<ResourceRequest> ResourceRequests => db.Set<ResourceRequest>();

        public DbSet<ResourceCandidate> ResourceCandidates => db.Set<ResourceCandidate>();

        public DbSet<ResourceTransfer> ResourceTransfers => db.Set<ResourceTransfer>();
    }
}
