using Gggs.Data;
using Gggs.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Infrastructure;

/// <summary>The Recruitment module's tables, in the <c>rec</c> schema.</summary>
internal sealed class RecruitmentModel : IModuleModel
{
    public const string Schema = "rec";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Requisition>(entity =>
        {
            entity.ToTable("Requisition", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.EmploymentTypeCode).HasMaxLength(64);
            entity.Property(row => row.WorkModeCode).HasMaxLength(64);
            entity.Property(row => row.MinExperienceYears).HasPrecision(4, 1);
            entity.Property(row => row.MaxExperienceYears).HasPrecision(4, 1);
            entity.Property(row => row.SalaryMin).HasPrecision(18, 2);
            entity.Property(row => row.SalaryMax).HasPrecision(18, 2);
            entity.Property(row => row.SkillsJson).IsRequired();
            entity.Property(row => row.Description).HasMaxLength(8000);
            entity.Property(row => row.Priority).HasMaxLength(20);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Requisition_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.ManpowerRequestId }).HasDatabaseName("IX_Requisition_TenantId_ManpowerRequest");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Requisition_TenantId_Status");
            entity.Ignore(row => row.Skills);
            entity.Ignore(row => row.Remaining);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<JobPosting>(entity =>
        {
            entity.ToTable("JobPosting", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(8000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasOne<Requisition>().WithMany().HasForeignKey(row => row.RequisitionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_JobPosting_TenantId_Status");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Agency>(entity =>
        {
            entity.ToTable("Agency", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.ContactName).HasMaxLength(200);
            entity.Property(row => row.Email).HasMaxLength(256);
            entity.Property(row => row.Phone).HasMaxLength(30);
            entity.Property(row => row.FeePercent).HasPrecision(5, 2);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.ToTable("Candidate", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.FirstName).HasMaxLength(100);
            entity.Property(row => row.LastName).HasMaxLength(100);
            entity.Property(row => row.Email).HasMaxLength(256);
            entity.Property(row => row.Mobile).HasMaxLength(15);
            entity.Property(row => row.CurrentCompany).HasMaxLength(200);
            entity.Property(row => row.CurrentTitle).HasMaxLength(200);
            entity.Property(row => row.ExperienceYears).HasPrecision(4, 1);
            entity.Property(row => row.CurrentCtc).HasPrecision(18, 2);
            entity.Property(row => row.ExpectedCtc).HasPrecision(18, 2);
            entity.Property(row => row.Location).HasMaxLength(200);
            entity.Property(row => row.SkillsJson).IsRequired();
            entity.Property(row => row.LinkedInUrl).HasMaxLength(500);
            entity.Property(row => row.Notes).HasMaxLength(4000);
            entity.Property(row => row.Source).HasConversion<byte>();
            entity.HasOne<Agency>().WithMany().HasForeignKey(row => row.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Candidate_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.Email }).HasDatabaseName("IX_Candidate_TenantId_Email");
            entity.HasIndex(row => new { row.TenantId, row.Mobile }).HasDatabaseName("IX_Candidate_TenantId_Mobile");
            entity.Ignore(row => row.FullName);
            entity.Ignore(row => row.Skills);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.ToTable("Application", Schema);
            entity.Property(row => row.Stage).HasConversion<byte>();
            entity.Property(row => row.RejectionReason).HasMaxLength(500);
            entity.Property(row => row.HistoryJson).IsRequired();
            entity.HasOne<Candidate>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Requisition>().WithMany().HasForeignKey(row => row.RequisitionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.RequisitionId, row.CandidateId })
                .HasDatabaseName("UX_Application_TenantId_Requisition_Candidate").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.CandidateId }).HasDatabaseName("IX_Application_TenantId_Candidate");
            entity.Ignore(row => row.History);
            entity.Ignore(row => row.IsClosed);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Interview>(entity =>
        {
            entity.ToTable("Interview", Schema);
            entity.Property(row => row.Round).HasMaxLength(100);
            entity.Property(row => row.Mode).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Location).HasMaxLength(500);
            entity.Property(row => row.PanelJson).IsRequired();
            entity.Property(row => row.Notes).HasMaxLength(2000);
            entity.HasOne<JobApplication>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.ApplicationId }).HasDatabaseName("IX_Interview_TenantId_Application");
            entity.HasIndex(row => new { row.TenantId, row.ScheduledAt }).HasDatabaseName("IX_Interview_TenantId_ScheduledAt");
            entity.Ignore(row => row.Panel);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Scorecard>(entity =>
        {
            entity.ToTable("Scorecard", Schema);
            entity.Property(row => row.RatingsJson).IsRequired();
            entity.Property(row => row.OverallRating).HasPrecision(4, 2);
            entity.Property(row => row.Recommendation).HasConversion<byte>();
            entity.Property(row => row.Comments).HasMaxLength(4000);
            entity.HasOne<Interview>().WithMany().HasForeignKey(row => row.InterviewId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.InterviewId, row.InterviewerEmployeeId })
                .HasDatabaseName("UX_Scorecard_TenantId_Interview_Interviewer").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.ApplicationId }).HasDatabaseName("IX_Scorecard_TenantId_Application");
            entity.Ignore(row => row.Ratings);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("Offer", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.AnnualCtc).HasPrecision(18, 2);
            entity.Property(row => row.Currency).HasMaxLength(3);
            entity.Property(row => row.Designation).HasMaxLength(200);
            entity.Property(row => row.Notes).HasMaxLength(2000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.DeclineReason).HasMaxLength(500);
            entity.HasOne<JobApplication>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Offer_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.ApplicationId }).HasDatabaseName("IX_Offer_TenantId_Application");
            entity.Ignore(row => row.IsLive);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<BackgroundCheck>(entity =>
        {
            entity.ToTable("BackgroundCheck", Schema);
            entity.Property(row => row.Vendor).HasMaxLength(200);
            entity.Property(row => row.ChecksJson).IsRequired();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Remarks).HasMaxLength(2000);
            entity.HasOne<Candidate>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CandidateId }).HasDatabaseName("IX_BackgroundCheck_TenantId_Candidate");
            entity.Ignore(row => row.Checks);
            entity.Ignore(row => row.IsComplete);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Referral>(entity =>
        {
            entity.ToTable("Referral", Schema);
            entity.Property(row => row.Note).HasMaxLength(1000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.BonusAmount).HasPrecision(18, 2);
            entity.HasOne<Candidate>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.ReferrerEmployeeId }).HasDatabaseName("IX_Referral_TenantId_Referrer");
            entity.HasIndex(row => new { row.TenantId, row.CandidateId }).HasDatabaseName("IX_Referral_TenantId_Candidate");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<RecruitmentSettings>(entity =>
        {
            entity.ToTable("RecruitmentSettings", Schema);
            entity.Property(row => row.ReferralBonus).HasPrecision(18, 2);
            entity.Property(row => row.Currency).HasMaxLength(3);
            entity.Property(row => row.CriteriaJson).IsRequired();
            entity.HasIndex(row => row.TenantId).HasDatabaseName("UX_RecruitmentSettings_TenantId").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class RecruitmentData
{
    extension(AppDbContext db)
    {
        public DbSet<Requisition> Requisitions => db.Set<Requisition>();

        public DbSet<JobPosting> JobPostings => db.Set<JobPosting>();

        public DbSet<Agency> Agencies => db.Set<Agency>();

        public DbSet<Candidate> Candidates => db.Set<Candidate>();

        public DbSet<JobApplication> JobApplications => db.Set<JobApplication>();

        public DbSet<Interview> Interviews => db.Set<Interview>();

        public DbSet<Scorecard> Scorecards => db.Set<Scorecard>();

        public DbSet<Offer> Offers => db.Set<Offer>();

        public DbSet<BackgroundCheck> BackgroundChecks => db.Set<BackgroundCheck>();

        public DbSet<Referral> Referrals => db.Set<Referral>();

        public DbSet<RecruitmentSettings> RecruitmentSettings => db.Set<RecruitmentSettings>();
    }
}
