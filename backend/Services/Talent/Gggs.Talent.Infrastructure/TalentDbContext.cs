using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

/// <summary>Talent's database: job requisitions, the postings made against them, candidates,
/// applications, the offers made against them, and the employee snapshot fed from Core HR.</summary>
public sealed class TalentDbContext(DbContextOptions<TalentDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "rec";

    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<Offer> Offers => Set<Offer>();

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

        modelBuilder.Entity<JobPosting>(entity =>
        {
            entity.ToTable("JobPosting");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DescriptionHtml).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Location).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.Slug })
                .HasDatabaseName("UX_JobPosting_TenantId_Slug")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.RequisitionId })
                .HasDatabaseName("IX_JobPosting_TenantId_RequisitionId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.ToTable("Candidate");
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(20);
            entity.Property(x => x.CurrentEmployer).HasMaxLength(200);
            entity.Property(x => x.Source).HasMaxLength(50);
            entity.Ignore(x => x.DisplayName);

            entity.HasIndex(x => new { x.TenantId, x.Email })
                .HasDatabaseName("UX_Candidate_TenantId_Email")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.ToTable("JobApplication");
            entity.Property(x => x.Stage).HasConversion<byte>();
            entity.Property(x => x.RejectionReason).HasMaxLength(500);

            entity.HasIndex(x => new { x.TenantId, x.CandidateId, x.PostingId })
                .HasDatabaseName("UX_JobApplication_TenantId_CandidateId_PostingId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.PostingId, x.Stage })
                .HasDatabaseName("IX_JobApplication_TenantId_PostingId_Stage");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("Offer");
            entity.Property(x => x.OfferNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.AnnualCtc).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.OfferNo })
                .HasDatabaseName("UX_Offer_TenantId_OfferNo")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.ApplicationId })
                .HasDatabaseName("UX_Offer_TenantId_ApplicationId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

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
