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
            entity.Property(invoice => invoice.CreditedAmount).HasPrecision(18, 2);
            entity.Ignore(invoice => invoice.OpenAmount);
            entity.Property(invoice => invoice.Notes).HasMaxLength(1000);
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.Number })
                .HasDatabaseName("UX_ProjectInvoice_TenantId_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.ProjectId }).HasDatabaseName("IX_ProjectInvoice_TenantId_Project");
            entity.Ignore(invoice => invoice.DomainEvents);
        });

        modelBuilder.Entity<ProjectInvoiceLine>(entity =>
        {
            entity.ToTable("ProjectInvoiceLine", Schema);
            entity.Property(line => line.Kind).HasConversion<byte>();
            entity.Property(line => line.Description).HasMaxLength(300);
            entity.Property(line => line.Quantity).HasPrecision(18, 2);
            entity.Property(line => line.Rate).HasPrecision(18, 2);
            entity.Property(line => line.Amount).HasPrecision(18, 2);
            entity.HasOne<ProjectInvoice>().WithMany().HasForeignKey(line => line.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => new { line.TenantId, line.ProjectId, line.Kind, line.Month }).HasDatabaseName("IX_ProjectInvoiceLine_TenantId_Project_Kind_Month");
            entity.Ignore(line => line.IsCredited);
            entity.Ignore(line => line.DomainEvents);
        });

        modelBuilder.Entity<ProjectBilledHours>(entity =>
        {
            entity.ToTable("ProjectBilledHours", Schema);
            entity.HasIndex(row => new { row.TenantId, row.TimesheetEntryId })
                .HasDatabaseName("UX_ProjectBilledHours_TenantId_Entry")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.ProjectId }).HasDatabaseName("IX_ProjectBilledHours_TenantId_Project");
            entity.HasIndex(row => new { row.TenantId, row.InvoiceId }).HasDatabaseName("IX_ProjectBilledHours_TenantId_Invoice");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ProjectCreditNote>(entity =>
        {
            entity.ToTable("ProjectCreditNote", Schema);
            entity.Property(note => note.Number).HasMaxLength(40);
            entity.Property(note => note.Amount).HasPrecision(18, 2);
            entity.Property(note => note.TaxAmount).HasPrecision(18, 2);
            entity.Property(note => note.Reason).HasMaxLength(500);
            entity.HasOne<ProjectInvoice>().WithMany().HasForeignKey(note => note.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(note => new { note.TenantId, note.Number })
                .HasDatabaseName("UX_ProjectCreditNote_TenantId_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(note => note.DomainEvents);
        });

        modelBuilder.Entity<ProjectFinanceProfile>(entity =>
        {
            entity.ToTable("ProjectFinanceProfile", Schema);
            entity.Property(profile => profile.ContractValue).HasPrecision(18, 2);
            entity.Property(profile => profile.RetainerMonthlyFee).HasPrecision(18, 2);
            entity.Property(profile => profile.NotToExceed).HasPrecision(18, 2);
            entity.Property(profile => profile.PoNumber).HasMaxLength(100);
            entity.Property(profile => profile.PoValue).HasPrecision(18, 2);
            entity.Property(profile => profile.IncludedHoursPerMonth).HasPrecision(8, 2);
            entity.Property(profile => profile.OverageRate).HasPrecision(18, 2);
            entity.Ignore(profile => profile.Terms);
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

        public DbSet<ProjectInvoiceLine> ProjectInvoiceLines => db.Set<ProjectInvoiceLine>();

        public DbSet<ProjectBilledHours> ProjectBilledHours => db.Set<ProjectBilledHours>();

        public DbSet<ProjectCreditNote> ProjectCreditNotes => db.Set<ProjectCreditNote>();

        public DbSet<ProjectFinanceProfile> ProjectFinanceProfiles => db.Set<ProjectFinanceProfile>();

        public DbSet<ProjectFinancialSnapshot> ProjectFinancialSnapshots => db.Set<ProjectFinancialSnapshot>();
    }
}
