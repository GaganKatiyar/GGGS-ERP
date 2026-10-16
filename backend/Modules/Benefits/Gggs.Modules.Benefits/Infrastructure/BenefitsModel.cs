using Gggs.Data;
using Gggs.Modules.Benefits.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Benefits.Infrastructure;

/// <summary>The Benefits module's tables, in the <c>ben</c> schema.</summary>
internal sealed class BenefitsModel : IModuleModel
{
    public const string Schema = "ben";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<BenefitPlan>(entity =>
        {
            entity.ToTable("BenefitPlan", Schema);
            entity.Property(plan => plan.Code).HasMaxLength(30);
            entity.Property(plan => plan.Name).HasMaxLength(200);
            entity.Property(plan => plan.Type).HasConversion<byte>();
            entity.Property(plan => plan.Provider).HasMaxLength(200);
            entity.Property(plan => plan.Description).HasMaxLength(2000);
            entity.Property(plan => plan.Currency).HasMaxLength(3);
            entity.Property(plan => plan.CoverageAmount).HasPrecision(18, 2);
            entity.Property(plan => plan.EmployeeContribution).HasPrecision(18, 2);
            entity.Property(plan => plan.EmployerContribution).HasPrecision(18, 2);
            entity.Property(plan => plan.DependentContribution).HasPrecision(18, 2);
            entity.Property(plan => plan.EligibleEmploymentTypes).HasMaxLength(500);
            entity.HasIndex(plan => new { plan.TenantId, plan.Code })
                .HasDatabaseName("UX_BenefitPlan_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(plan => plan.EmploymentTypes);
            entity.Ignore(plan => plan.DomainEvents);
        });

        modelBuilder.Entity<BenefitEnrollment>(entity =>
        {
            entity.ToTable("BenefitEnrollment", Schema);
            entity.Property(enrollment => enrollment.Status).HasConversion<byte>();
            entity.Property(enrollment => enrollment.DependentIdsJson).IsRequired();
            entity.Property(enrollment => enrollment.EmployeeMonthly).HasPrecision(18, 2);
            entity.Property(enrollment => enrollment.EmployerMonthly).HasPrecision(18, 2);
            entity.Property(enrollment => enrollment.Notes).HasMaxLength(500);
            entity.HasOne<BenefitPlan>().WithMany().HasForeignKey(enrollment => enrollment.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(enrollment => new { enrollment.TenantId, enrollment.EmployeeId, enrollment.PlanId }).HasDatabaseName("IX_BenefitEnrollment_TenantId_Employee_Plan");
            entity.HasIndex(enrollment => new { enrollment.TenantId, enrollment.PlanId, enrollment.Status }).HasDatabaseName("IX_BenefitEnrollment_TenantId_Plan_Status");
            entity.Ignore(enrollment => enrollment.DependentIds);
            entity.Ignore(enrollment => enrollment.DomainEvents);
        });
    }
}

internal static class BenefitsData
{
    extension(AppDbContext db)
    {
        public DbSet<BenefitPlan> BenefitPlans => db.Set<BenefitPlan>();

        public DbSet<BenefitEnrollment> BenefitEnrollments => db.Set<BenefitEnrollment>();
    }
}
