using Gggs.Data;
using Gggs.Modules.ProjectFinancials.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.ProjectFinancials.Infrastructure;

/// <summary>The ProjectFinancials module's tables, in the <c>pfin</c> schema.</summary>
internal sealed class FinanceModel : IModuleModel
{
    public const string Schema = "pfin";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProjectBudgetLine>(entity =>
        {
            entity.ToTable("ProjectBudgetLine", Schema);
            entity.Property(line => line.Category).HasConversion<byte>();
            entity.Property(line => line.Amount).HasPrecision(18, 2);
            entity.Property(line => line.Notes).HasMaxLength(500);
            entity.HasIndex(line => new { line.TenantId, line.ProjectId, line.Category })
                .HasDatabaseName("UX_ProjectBudgetLine_TenantId_Project_Category")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(line => line.DomainEvents);
        });

        modelBuilder.Entity<ProjectRate>(entity =>
        {
            entity.ToTable("ProjectRate", Schema);
            entity.Property(rate => rate.BillingRate).HasPrecision(18, 2);
            entity.Property(rate => rate.CostRate).HasPrecision(18, 2);
            entity.HasIndex(rate => new { rate.TenantId, rate.ProjectId }).HasDatabaseName("IX_ProjectRate_TenantId_Project");
            entity.Ignore(rate => rate.DomainEvents);
        });

        modelBuilder.Entity<ProjectCost>(entity =>
        {
            entity.ToTable("ProjectCost", Schema);
            entity.Property(cost => cost.Category).HasConversion<byte>();
            entity.Property(cost => cost.Amount).HasPrecision(18, 2);
            entity.Property(cost => cost.Vendor).HasMaxLength(200);
            entity.Property(cost => cost.Description).HasMaxLength(300);
            entity.Property(cost => cost.Reference).HasMaxLength(100);
            entity.HasIndex(cost => new { cost.TenantId, cost.ProjectId, cost.Date }).HasDatabaseName("IX_ProjectCost_TenantId_Project_Date");
            entity.Ignore(cost => cost.DomainEvents);
        });

        modelBuilder.Entity<ProjectInvoice>(entity =>
        {
            entity.ToTable("ProjectInvoice", Schema);
            entity.Property(invoice => invoice.Number).HasMaxLength(40);
            entity.Property(invoice => invoice.Status).HasConversion<byte>();
            entity.Property(invoice => invoice.Amount).HasPrecision(18, 2);
            entity.Property(invoice => invoice.TaxAmount).HasPrecision(18, 2);
            entity.Property(invoice => invoice.Notes).HasMaxLength(1000);
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.Number })
                .HasDatabaseName("UX_ProjectInvoice_TenantId_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.ProjectId }).HasDatabaseName("IX_ProjectInvoice_TenantId_Project");
            entity.Ignore(invoice => invoice.DomainEvents);
        });

        modelBuilder.Entity<ProjectFinanceProfile>(entity =>
        {
            entity.ToTable("ProjectFinanceProfile", Schema);
            entity.Property(profile => profile.ContractValue).HasPrecision(18, 2);
            entity.Property(profile => profile.RetainerMonthlyFee).HasPrecision(18, 2);
            entity.Property(profile => profile.MilestoneAmountsJson).IsRequired();
            entity.HasIndex(profile => new { profile.TenantId, profile.ProjectId })
                .HasDatabaseName("UX_ProjectFinanceProfile_TenantId_Project")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(profile => profile.MilestoneAmounts);
            entity.Ignore(profile => profile.DomainEvents);
        });

        modelBuilder.Entity<ProjectFinancialSnapshot>(entity =>
        {
            entity.ToTable("ProjectFinancialSnapshot", Schema);
            entity.Property(snapshot => snapshot.Revenue).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.Cost).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.Billed).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.Collected).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.ForecastCost).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.ForecastRevenue).HasPrecision(18, 2);
            entity.HasIndex(snapshot => new { snapshot.TenantId, snapshot.ProjectId, snapshot.Month })
                .HasDatabaseName("UX_ProjectFinancialSnapshot_TenantId_Project_Month")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(snapshot => snapshot.DomainEvents);
        });
    }
}

internal static class FinanceData
{
    extension(AppDbContext db)
    {
        public DbSet<ProjectBudgetLine> ProjectBudgetLines => db.Set<ProjectBudgetLine>();

        public DbSet<ProjectRate> ProjectRates => db.Set<ProjectRate>();

        public DbSet<ProjectCost> ProjectCosts => db.Set<ProjectCost>();

        public DbSet<ProjectInvoice> ProjectInvoices => db.Set<ProjectInvoice>();

        public DbSet<ProjectFinanceProfile> ProjectFinanceProfiles => db.Set<ProjectFinanceProfile>();

        public DbSet<ProjectFinancialSnapshot> ProjectFinancialSnapshots => db.Set<ProjectFinancialSnapshot>();
    }
}
