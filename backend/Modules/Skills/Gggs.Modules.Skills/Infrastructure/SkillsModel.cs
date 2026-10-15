using Gggs.Data;
using Gggs.Modules.Skills.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Skills.Infrastructure;

/// <summary>The Skills module's tables, in the <c>skl</c> schema.</summary>
internal sealed class SkillsModel : IModuleModel
{
    public const string Schema = "skl";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.ToTable("Skill", Schema);
            entity.Property(skill => skill.Code).HasMaxLength(32).IsRequired();
            entity.Property(skill => skill.Name).HasMaxLength(100).IsRequired();
            entity.Property(skill => skill.Category).HasMaxLength(50).IsRequired();
            entity.Property(skill => skill.Description).HasMaxLength(500);
            entity.HasIndex(skill => new { skill.TenantId, skill.Code })
                .HasDatabaseName("UX_Skill_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(skill => skill.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSkill>(entity =>
        {
            entity.ToTable("EmployeeSkill", Schema);
            entity.Property(row => row.YearsExperience).HasPrecision(4, 1);
            entity.Property(row => row.Source).HasConversion<byte>();
            entity.HasOne<Skill>().WithMany().HasForeignKey(row => row.SkillId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.SkillId })
                .HasDatabaseName("UX_EmployeeSkill_TenantId_Employee_Skill")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.SkillId, row.CurrentLevel }).HasDatabaseName("IX_EmployeeSkill_TenantId_Skill_Level");
            entity.Ignore(row => row.Gap);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<EmployeeCertification>(entity =>
        {
            entity.ToTable("EmployeeCertification", Schema);
            entity.Property(row => row.Name).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Issuer).HasMaxLength(200);
            entity.Property(row => row.CredentialId).HasMaxLength(100);
            entity.HasOne<Skill>().WithMany().HasForeignKey(row => row.SkillId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_EmployeeCertification_TenantId_Employee");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class SkillsData
{
    extension(AppDbContext db)
    {
        public DbSet<Skill> Skills => db.Set<Skill>();

        public DbSet<EmployeeSkill> EmployeeSkills => db.Set<EmployeeSkill>();

        public DbSet<EmployeeCertification> EmployeeCertifications => db.Set<EmployeeCertification>();
    }
}
