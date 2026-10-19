using Gggs.Data;
using Gggs.Modules.Career.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Career.Infrastructure;

/// <summary>The Career module's tables, in the <c>car</c> schema.</summary>
internal sealed class CareerModel : IModuleModel
{
    public const string Schema = "car";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<CareerPath>(entity =>
        {
            entity.ToTable("Path", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(2000);
            entity.Property(row => row.Track).HasConversion<byte>();
            entity.Property(row => row.StepsJson).IsRequired();
            entity.Ignore(row => row.Steps);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<PromotionNomination>(entity =>
        {
            entity.ToTable("Promotion", Schema);
            entity.Property(row => row.CurrentDesignation).HasMaxLength(200);
            entity.Property(row => row.CurrentAnnualCtc).HasPrecision(18, 2);
            entity.Property(row => row.ProposedDesignation).HasMaxLength(200);
            entity.Property(row => row.ProposedAnnualCtc).HasPrecision(18, 2);
            entity.Property(row => row.Justification).HasMaxLength(4000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Promotion_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Promotion_TenantId_Status");
            entity.Ignore(row => row.IncreasePercent);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<SuccessionPlan>(entity =>
        {
            entity.ToTable("SuccessionPlan", Schema);
            entity.Property(row => row.RoleTitle).HasMaxLength(200);
            entity.Property(row => row.Criticality).HasConversion<byte>();
            entity.Property(row => row.VacancyRisk).HasConversion<byte>();
            entity.Property(row => row.Notes).HasMaxLength(2000);
            entity.Property(row => row.SuccessorsJson).IsRequired();
            entity.Ignore(row => row.Successors);
            entity.Ignore(row => row.Covered);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<TalentAssessment>(entity =>
        {
            entity.ToTable("TalentAssessment", Schema);
            entity.Property(row => row.Potential).HasConversion<byte>();
            entity.Property(row => row.FlightRisk).HasConversion<byte>();
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.AssessedOn }).HasDatabaseName("IX_TalentAssessment_TenantId_Employee_On");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class CareerData
{
    extension(AppDbContext db)
    {
        public DbSet<CareerPath> CareerPaths => db.Set<CareerPath>();

        public DbSet<PromotionNomination> Promotions => db.Set<PromotionNomination>();

        public DbSet<SuccessionPlan> SuccessionPlans => db.Set<SuccessionPlan>();

        public DbSet<TalentAssessment> TalentAssessments => db.Set<TalentAssessment>();
    }
}
