using Gggs.Data;
using Gggs.Modules.Engagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Engagement.Infrastructure;

/// <summary>The Engagement module's tables, in the <c>eng</c> schema.</summary>
internal sealed class EngagementModel : IModuleModel
{
    public const string Schema = "eng";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Survey>(entity =>
        {
            entity.ToTable("Survey", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(2000);
            entity.Property(row => row.QuestionsJson).IsRequired();
            entity.Property(row => row.AudienceJson).IsRequired();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Survey_TenantId_Status");
            entity.Ignore(row => row.Questions);
            entity.Ignore(row => row.AudienceDepartmentIds);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<SurveyInvitation>(entity =>
        {
            entity.ToTable("SurveyInvitation", Schema);
            entity.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.SurveyId, row.EmployeeId }).HasDatabaseName("UX_SurveyInvitation_TenantId_Survey_Employee").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_SurveyInvitation_TenantId_Employee");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<SurveyResponse>(entity =>
        {
            entity.ToTable("SurveyResponse", Schema);
            entity.Property(row => row.AnswersJson).IsRequired();
            entity.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.SurveyId }).HasDatabaseName("IX_SurveyResponse_TenantId_Survey");
            entity.Ignore(row => row.Answers);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<FeatureBanner>(entity =>
        {
            entity.ToTable("FeatureBanner", Schema);
            entity.Property(row => row.Title).HasMaxLength(120);
            entity.Property(row => row.Body).HasMaxLength(300);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.Layout).HasConversion<byte>();
            entity.Property(row => row.ActionLabel).HasMaxLength(40);
            entity.Property(row => row.ActionLink).HasMaxLength(300);
            entity.Property(row => row.SecondaryLabel).HasMaxLength(40);
            entity.Property(row => row.SecondaryLink).HasMaxLength(300);
            entity.Property(row => row.AudienceDepartmentIdsJson).IsRequired();
            entity.Property(row => row.AudienceLocationIdsJson).IsRequired();
            entity.HasIndex(row => new { row.TenantId, row.IsPublished, row.StartsOn }).HasDatabaseName("IX_FeatureBanner_TenantId_Published_StartsOn");
            entity.Ignore(row => row.AudienceDepartmentIds);
            entity.Ignore(row => row.AudienceLocationIds);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<CelebrationWish>(entity =>
        {
            entity.ToTable("CelebrationWish", Schema);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.Note).HasMaxLength(CelebrationWish.NoteMaxLength);
            entity.Property(row => row.SentByName).HasMaxLength(200);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.Kind, row.OccurredOn })
                .HasDatabaseName("UX_CelebrationWish_TenantId_Employee_Kind_Day").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.Kind, row.OccurredOn }).HasDatabaseName("IX_CelebrationWish_TenantId_Kind_Day");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class EngagementData
{
    extension(AppDbContext db)
    {
        public DbSet<Survey> Surveys => db.Set<Survey>();

        public DbSet<SurveyInvitation> SurveyInvitations => db.Set<SurveyInvitation>();

        public DbSet<SurveyResponse> SurveyResponses => db.Set<SurveyResponse>();

        public DbSet<FeatureBanner> FeatureBanners => db.Set<FeatureBanner>();

        public DbSet<CelebrationWish> CelebrationWishes => db.Set<CelebrationWish>();
    }
}
