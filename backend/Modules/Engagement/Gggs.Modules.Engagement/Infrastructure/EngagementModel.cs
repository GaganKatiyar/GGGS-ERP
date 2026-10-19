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
    }
}

internal static class EngagementData
{
    extension(AppDbContext db)
    {
        public DbSet<Survey> Surveys => db.Set<Survey>();

        public DbSet<SurveyInvitation> SurveyInvitations => db.Set<SurveyInvitation>();

        public DbSet<SurveyResponse> SurveyResponses => db.Set<SurveyResponse>();
    }
}
