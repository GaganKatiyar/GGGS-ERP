using Gggs.Data;
using Gggs.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Learning.Infrastructure;

/// <summary>The Learning module's tables, in the <c>lms</c> schema.</summary>
internal sealed class LearningModel : IModuleModel
{
    public const string Schema = "lms";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable("Course", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(4000);
            entity.Property(row => row.Category).HasMaxLength(80);
            entity.Property(row => row.Format).HasConversion<byte>();
            entity.Property(row => row.Provider).HasMaxLength(200);
            entity.Property(row => row.DurationHours).HasPrecision(6, 1);
            entity.Property(row => row.ContentUrl).HasMaxLength(500);
            entity.Property(row => row.SkillsJson).IsRequired();
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_Course_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.Skills);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<LearningPath>(entity =>
        {
            entity.ToTable("Path", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(2000);
            entity.Property(row => row.CoursesJson).IsRequired();
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_Path_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.CourseIds);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("Enrollment", Schema);
            entity.Property(row => row.Source).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Score).HasPrecision(5, 2);
            entity.Property(row => row.CertificateNumber).HasMaxLength(40);
            entity.Property(row => row.Feedback).HasMaxLength(1000);
            entity.HasOne<Course>().WithMany().HasForeignKey(row => row.CourseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.Status }).HasDatabaseName("IX_Enrollment_TenantId_Employee_Status");
            entity.HasIndex(row => new { row.TenantId, row.CourseId }).HasDatabaseName("IX_Enrollment_TenantId_Course");
            entity.HasIndex(row => new { row.TenantId, row.CertificateExpiresOn }).HasDatabaseName("IX_Enrollment_TenantId_CertificateExpiresOn");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class LearningData
{
    extension(AppDbContext db)
    {
        public DbSet<Course> Courses => db.Set<Course>();

        public DbSet<LearningPath> LearningPaths => db.Set<LearningPath>();

        public DbSet<Enrollment> Enrollments => db.Set<Enrollment>();
    }
}
