using Gggs.Data;
using Gggs.Modules.Performance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Performance.Infrastructure;

/// <summary>The Performance module's tables: reviews in <c>perf</c>, goals in <c>goal</c>.</summary>
internal sealed class PerformanceModel : IModuleModel
{
    public const string Schema = "perf";
    public const string GoalSchema = "goal";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Goal>(entity =>
        {
            entity.ToTable("Goal", GoalSchema);
            entity.Property(row => row.Title).HasMaxLength(300);
            entity.Property(row => row.Description).HasMaxLength(4000);
            entity.Property(row => row.Type).HasConversion<byte>();
            entity.Property(row => row.Level).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Confidence).HasConversion<byte?>();
            entity.Property(row => row.Weight).HasPrecision(5, 2);
            entity.Property(row => row.Progress).HasPrecision(5, 1);
            entity.HasOne<Goal>().WithMany().HasForeignKey(row => row.ParentGoalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.OwnerEmployeeId }).HasDatabaseName("IX_Goal_TenantId_Owner");
            entity.HasIndex(row => new { row.TenantId, row.ParentGoalId }).HasDatabaseName("IX_Goal_TenantId_Parent");
            entity.HasIndex(row => new { row.TenantId, row.CycleId }).HasDatabaseName("IX_Goal_TenantId_Cycle");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<KeyResult>(entity =>
        {
            entity.ToTable("KeyResult", GoalSchema);
            entity.Property(row => row.Title).HasMaxLength(300);
            entity.Property(row => row.MetricType).HasConversion<byte>();
            entity.Property(row => row.StartValue).HasPrecision(18, 2);
            entity.Property(row => row.TargetValue).HasPrecision(18, 2);
            entity.Property(row => row.CurrentValue).HasPrecision(18, 2);
            entity.Property(row => row.Unit).HasMaxLength(20);
            entity.Property(row => row.Weight).HasPrecision(5, 2);
            entity.HasOne<Goal>().WithMany().HasForeignKey(row => row.GoalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.GoalId }).HasDatabaseName("IX_KeyResult_TenantId_Goal");
            entity.Ignore(row => row.Progress);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<GoalCheckIn>(entity =>
        {
            entity.ToTable("CheckIn", GoalSchema);
            entity.Property(row => row.Progress).HasPrecision(5, 1);
            entity.Property(row => row.Confidence).HasConversion<byte>();
            entity.Property(row => row.Note).HasMaxLength(2000);
            entity.Property(row => row.ValuesJson).IsRequired();
            entity.HasOne<Goal>().WithMany().HasForeignKey(row => row.GoalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.GoalId }).HasDatabaseName("IX_CheckIn_TenantId_Goal");
            entity.Ignore(row => row.Values);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ReviewCycle>(entity =>
        {
            entity.ToTable("Cycle", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Type).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.GoalWeight).HasPrecision(5, 2);
            entity.Property(row => row.CompetenciesJson).IsRequired();
            entity.Property(row => row.DepartmentsJson).IsRequired();
            entity.Ignore(row => row.Competencies);
            entity.Ignore(row => row.DepartmentIds);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Review", Schema);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.SelfJson).IsRequired();
            entity.Property(row => row.ManagerJson).IsRequired();
            entity.Property(row => row.GoalAchievement).HasPrecision(5, 1);
            entity.Property(row => row.Score).HasPrecision(4, 2);
            entity.Property(row => row.CalibrationNote).HasMaxLength(1000);
            entity.Property(row => row.EmployeeComment).HasMaxLength(2000);
            entity.HasOne<ReviewCycle>().WithMany().HasForeignKey(row => row.CycleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CycleId, row.EmployeeId }).HasDatabaseName("UX_Review_TenantId_Cycle_Employee").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Review_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.ManagerEmployeeId }).HasDatabaseName("IX_Review_TenantId_Manager");
            entity.Ignore(row => row.Self);
            entity.Ignore(row => row.Manager);
            entity.Ignore(row => row.ManagerRating);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<FeedbackRequest>(entity =>
        {
            entity.ToTable("Feedback", Schema);
            entity.Property(row => row.Relationship).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.RatingsJson).IsRequired();
            entity.Property(row => row.Strengths).HasMaxLength(4000);
            entity.Property(row => row.Improvements).HasMaxLength(4000);
            entity.HasOne<Review>().WithMany().HasForeignKey(row => row.ReviewId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.ReviewId, row.ReviewerEmployeeId }).HasDatabaseName("UX_Feedback_TenantId_Review_Reviewer").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.ReviewerEmployeeId, row.Status }).HasDatabaseName("IX_Feedback_TenantId_Reviewer_Status");
            entity.Ignore(row => row.Ratings);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ImprovementPlan>(entity =>
        {
            entity.ToTable("ImprovementPlan", Schema);
            entity.Property(row => row.Reason).HasMaxLength(2000);
            entity.Property(row => row.ObjectivesJson).IsRequired();
            entity.Property(row => row.NotesJson).IsRequired();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Outcome).HasMaxLength(2000);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_ImprovementPlan_TenantId_Employee");
            entity.Ignore(row => row.Objectives);
            entity.Ignore(row => row.Notes);
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class PerformanceData
{
    extension(AppDbContext db)
    {
        public DbSet<Goal> Goals => db.Set<Goal>();

        public DbSet<KeyResult> KeyResults => db.Set<KeyResult>();

        public DbSet<GoalCheckIn> GoalCheckIns => db.Set<GoalCheckIn>();

        public DbSet<ReviewCycle> ReviewCycles => db.Set<ReviewCycle>();

        public DbSet<Review> Reviews => db.Set<Review>();

        public DbSet<FeedbackRequest> FeedbackRequests => db.Set<FeedbackRequest>();

        public DbSet<ImprovementPlan> ImprovementPlans => db.Set<ImprovementPlan>();
    }
}
