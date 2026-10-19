using Gggs.Data;
using Gggs.Modules.Rewards.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Rewards.Infrastructure;

/// <summary>The Rewards module's tables, in the <c>rwd</c> schema.</summary>
internal sealed class RewardModel : IModuleModel
{
    public const string Schema = "rwd";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RewardSettings>(entity =>
        {
            entity.ToTable("Settings", Schema);
            entity.Property(row => row.PointValue).HasPrecision(18, 4);
            entity.Property(row => row.Currency).HasMaxLength(3);
            entity.HasIndex(row => row.TenantId).HasDatabaseName("UX_Settings_TenantId").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<RecognitionType>(entity =>
        {
            entity.ToTable("RecognitionType", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Description).HasMaxLength(500);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.CashAmount).HasPrecision(18, 2);
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_RecognitionType_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<RewardRule>(entity =>
        {
            entity.ToTable("Rule", Schema);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Trigger).HasConversion<byte>();
            entity.Property(row => row.Message).HasMaxLength(300);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Recognition>(entity =>
        {
            entity.ToTable("Recognition", Schema);
            entity.Property(row => row.Message).HasMaxLength(1000);
            entity.Property(row => row.Kind).HasConversion<byte?>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.CashAmount).HasPrecision(18, 2);
            entity.Property(row => row.SourceKey).HasMaxLength(100);
            entity.HasOne<RecognitionType>().WithMany().HasForeignKey(row => row.TypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RewardRule>().WithMany().HasForeignKey(row => row.RuleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RewardBudget>().WithMany().HasForeignKey(row => row.BudgetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Status, row.GivenOn }).HasDatabaseName("IX_Recognition_TenantId_Status_GivenOn");
            entity.HasIndex(row => new { row.TenantId, row.RecipientEmployeeId }).HasDatabaseName("IX_Recognition_TenantId_Recipient");
            entity.HasIndex(row => new { row.TenantId, row.GiverEmployeeId, row.GivenOn }).HasDatabaseName("IX_Recognition_TenantId_Giver_GivenOn");
            entity.HasIndex(row => new { row.TenantId, row.RuleId, row.RecipientEmployeeId, row.SourceKey })
                .HasDatabaseName("UX_Recognition_TenantId_Rule_Recipient_Source").IsUnique().HasFilter("[RuleId] IS NOT NULL AND [IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<RecognitionCheer>(entity =>
        {
            entity.ToTable("RecognitionCheer", Schema);
            entity.HasOne<Recognition>().WithMany().HasForeignKey(row => row.RecognitionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.RecognitionId, row.EmployeeId }).HasDatabaseName("UX_RecognitionCheer_TenantId_Recognition_Employee").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<PointEntry>(entity =>
        {
            entity.ToTable("PointEntry", Schema);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.SourceType).HasMaxLength(40);
            entity.Property(row => row.Note).HasMaxLength(300);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.OccurredOn }).HasDatabaseName("IX_PointEntry_TenantId_Employee_OccurredOn");
            entity.HasIndex(row => new { row.TenantId, row.ExpiresOn }).HasDatabaseName("IX_PointEntry_TenantId_ExpiresOn").HasFilter("[RemainingPoints] > 0");
            entity.Ignore(row => row.IsCredit);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<CatalogItem>(entity =>
        {
            entity.ToTable("CatalogItem", Schema);
            entity.Property(row => row.Name).HasMaxLength(150);
            entity.Property(row => row.Description).HasMaxLength(1000);
            entity.Property(row => row.Category).HasMaxLength(60);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Redemption>(entity =>
        {
            entity.ToTable("Redemption", Schema);
            entity.Property(row => row.ItemName).HasMaxLength(150);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Note).HasMaxLength(500);
            entity.HasOne<CatalogItem>().WithMany().HasForeignKey(row => row.ItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Redemption_TenantId_Status");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Redemption_TenantId_Employee");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<RewardBudget>(entity =>
        {
            entity.ToTable("Budget", Schema);
            entity.HasIndex(row => new { row.TenantId, row.Year, row.DepartmentId }).HasDatabaseName("UX_Budget_TenantId_Year_Department").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class RewardData
{
    extension(AppDbContext db)
    {
        public DbSet<RewardSettings> RewardSettings => db.Set<RewardSettings>();

        public DbSet<RecognitionType> RecognitionTypes => db.Set<RecognitionType>();

        public DbSet<RewardRule> RewardRules => db.Set<RewardRule>();

        public DbSet<Recognition> Recognitions => db.Set<Recognition>();

        public DbSet<RecognitionCheer> RecognitionCheers => db.Set<RecognitionCheer>();

        public DbSet<PointEntry> PointEntries => db.Set<PointEntry>();

        public DbSet<CatalogItem> CatalogItems => db.Set<CatalogItem>();

        public DbSet<Redemption> Redemptions => db.Set<Redemption>();

        public DbSet<RewardBudget> RewardBudgets => db.Set<RewardBudget>();
    }
}
