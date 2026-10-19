using Gggs.Data;
using Gggs.Modules.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Onboarding.Infrastructure;

/// <summary>The Onboarding module's tables, in the <c>onb</c> schema.</summary>
internal sealed class OnboardingModel : IModuleModel
{
    public const string Schema = "onb";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OnboardingTemplate>(entity =>
        {
            entity.ToTable("Template", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(1000);
            entity.Property(row => row.EmploymentTypeCode).HasMaxLength(64);
            entity.Property(row => row.TasksJson).IsRequired();
            entity.Ignore(row => row.Tasks);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<OnboardingCase>(entity =>
        {
            entity.ToTable("Case", Schema);
            entity.Property(row => row.TemplateName).HasMaxLength(200);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Case_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Case_TenantId_Status");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<OnboardingTask>(entity =>
        {
            entity.ToTable("Task", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(1000);
            entity.Property(row => row.Category).HasConversion<byte>();
            entity.Property(row => row.AssigneeRole).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.HasOne<OnboardingCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("IX_Task_TenantId_Case");
            entity.HasIndex(row => new { row.TenantId, row.AssigneeEmployeeId, row.Status }).HasDatabaseName("IX_Task_TenantId_Assignee_Status");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<OnboardingSettings>(entity =>
        {
            entity.ToTable("Settings", Schema);
            entity.HasIndex(row => row.TenantId).HasDatabaseName("UX_Settings_TenantId").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class OnboardingData
{
    extension(AppDbContext db)
    {
        public DbSet<OnboardingTemplate> OnboardingTemplates => db.Set<OnboardingTemplate>();

        public DbSet<OnboardingCase> OnboardingCases => db.Set<OnboardingCase>();

        public DbSet<OnboardingTask> OnboardingTasks => db.Set<OnboardingTask>();

        public DbSet<OnboardingSettings> OnboardingSettings => db.Set<OnboardingSettings>();
    }
}
