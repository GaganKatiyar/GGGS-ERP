using Gggs.Data;
using Gggs.Modules.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workforce.Infrastructure;

/// <summary>The Workforce module's tables, in the <c>wfp</c> schema.</summary>
internal sealed class WorkforceModel : IModuleModel
{
    public const string Schema = "wfp";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WorkforcePlan>(entity =>
        {
            entity.ToTable("WorkforcePlan", Schema);
            entity.Property(plan => plan.Name).HasMaxLength(200);
            entity.Property(plan => plan.Status).HasConversion<byte>();
            entity.Property(plan => plan.Notes).HasMaxLength(2000);
            entity.HasIndex(plan => new { plan.TenantId, plan.Year }).HasDatabaseName("IX_WorkforcePlan_TenantId_Year");
            entity.Ignore(plan => plan.DomainEvents);
        });

        modelBuilder.Entity<WorkforcePlanLine>(entity =>
        {
            entity.ToTable("WorkforcePlanLine", Schema);
            entity.Property(line => line.SalaryBudget).HasPrecision(18, 2);
            entity.Property(line => line.HiringBudget).HasPrecision(18, 2);
            entity.Property(line => line.Notes).HasMaxLength(500);
            entity.HasOne<WorkforcePlan>().WithMany().HasForeignKey(line => line.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => new { line.TenantId, line.PlanId }).HasDatabaseName("IX_WorkforcePlanLine_TenantId_Plan");
            entity.Ignore(line => line.HeadcountGap);
            entity.Ignore(line => line.DomainEvents);
        });

        modelBuilder.Entity<ManpowerRequest>(entity =>
        {
            entity.ToTable("ManpowerRequest", Schema);
            entity.Property(request => request.Number).HasMaxLength(30);
            entity.Property(request => request.Title).HasMaxLength(200);
            entity.Property(request => request.SkillsJson).IsRequired();
            entity.Property(request => request.MinExperienceYears).HasPrecision(4, 1);
            entity.Property(request => request.MaxExperienceYears).HasPrecision(4, 1);
            entity.Property(request => request.EmploymentTypeCode).HasMaxLength(64);
            entity.Property(request => request.WorkModeCode).HasMaxLength(64);
            entity.Property(request => request.SalaryMin).HasPrecision(18, 2);
            entity.Property(request => request.SalaryMax).HasPrecision(18, 2);
            entity.Property(request => request.Budget).HasPrecision(18, 2);
            entity.Property(request => request.Priority).HasConversion<byte>();
            entity.Property(request => request.Reason).HasConversion<byte>();
            entity.Property(request => request.Status).HasConversion<byte>();
            entity.Property(request => request.Justification).HasMaxLength(2000);
            entity.HasIndex(request => new { request.TenantId, request.Number })
                .HasDatabaseName("UX_ManpowerRequest_TenantId_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(request => new { request.TenantId, request.DepartmentId, request.Status }).HasDatabaseName("IX_ManpowerRequest_TenantId_Department_Status");
            entity.Ignore(request => request.Skills);
            entity.Ignore(request => request.DomainEvents);
        });
    }
}

internal static class WorkforceData
{
    extension(AppDbContext db)
    {
        public DbSet<WorkforcePlan> WorkforcePlans => db.Set<WorkforcePlan>();

        public DbSet<WorkforcePlanLine> WorkforcePlanLines => db.Set<WorkforcePlanLine>();

        public DbSet<ManpowerRequest> ManpowerRequests => db.Set<ManpowerRequest>();
    }
}
