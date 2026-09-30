using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Talent.Domain.Learning;
using Gggs.Talent.Domain.Onboarding;
using Gggs.Talent.Domain.Performance;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

/// <summary>Talent's database: job requisitions, the postings made against them, candidates,
/// applications, the offers made against them, onboarding templates and cases, performance cycles
/// with goals/check-ins and reviews, courses and enrollments, and the employee snapshot fed from
/// Core HR.</summary>
public sealed class TalentDbContext(DbContextOptions<TalentDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "rec";

    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<Offer> Offers => Set<Offer>();

    public DbSet<OnboardingTemplate> OnboardingTemplates => Set<OnboardingTemplate>();

    public DbSet<OnboardingTemplateTask> OnboardingTemplateTasks => Set<OnboardingTemplateTask>();

    public DbSet<OnboardingCase> OnboardingCases => Set<OnboardingCase>();

    public DbSet<OnboardingTask> OnboardingTasks => Set<OnboardingTask>();

    public DbSet<PerformanceCycle> PerformanceCycles => Set<PerformanceCycle>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalCheckIn> GoalCheckIns => Set<GoalCheckIn>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

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

        modelBuilder.Entity<OnboardingTemplate>(entity =>
        {
            entity.ToTable("OnboardingTemplate", "onb");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OnboardingTemplateTask>(entity =>
        {
            entity.ToTable("OnboardingTemplateTask", "onb");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AssigneeRole).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.TemplateId })
                .HasDatabaseName("IX_OnboardingTemplateTask_TenantId_TemplateId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OnboardingCase>(entity =>
        {
            entity.ToTable("OnboardingCase", "onb");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("IX_OnboardingCase_TenantId_EmployeeId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OnboardingTask>(entity =>
        {
            entity.ToTable("OnboardingTask", "onb");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AssigneeRole).HasConversion<byte>();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.CaseId })
                .HasDatabaseName("IX_OnboardingTask_TenantId_CaseId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PerformanceCycle>(entity =>
        {
            entity.ToTable("PerformanceCycle", "perf");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Goal>(entity =>
        {
            entity.ToTable("Goal", "perf");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.TargetValue).HasColumnType("decimal(12,2)");
            entity.Property(x => x.CurrentValue).HasColumnType("decimal(12,2)");
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.CycleId, x.EmployeeId })
                .HasDatabaseName("IX_Goal_TenantId_CycleId_EmployeeId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<GoalCheckIn>(entity =>
        {
            entity.ToTable("GoalCheckIn", "perf");
            entity.Property(x => x.Value).HasColumnType("decimal(12,2)");
            entity.Property(x => x.Comment).HasMaxLength(500);

            entity.HasIndex(x => new { x.TenantId, x.GoalId, x.CheckInDate })
                .HasDatabaseName("IX_GoalCheckIn_TenantId_GoalId_CheckInDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Review", "perf");
            entity.Property(x => x.SelfComments).HasMaxLength(2000);
            entity.Property(x => x.ManagerComments).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CycleId })
                .HasDatabaseName("UX_Review_TenantId_EmployeeId_CycleId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.ManagerEmployeeId, x.Status })
                .HasDatabaseName("IX_Review_TenantId_ManagerEmployeeId_Status");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable("Course", "lms");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DurationHours).HasColumnType("decimal(6,2)");

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Course_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("Enrollment", "lms");
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.Score).HasColumnType("decimal(5,2)");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status })
                .HasDatabaseName("IX_Enrollment_TenantId_EmployeeId_Status");

            entity.HasIndex(x => new { x.TenantId, x.CourseId })
                .HasDatabaseName("IX_Enrollment_TenantId_CourseId");

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
