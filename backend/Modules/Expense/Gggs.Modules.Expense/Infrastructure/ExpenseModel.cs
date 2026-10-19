using Gggs.Data;
using Gggs.Modules.Expense.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Expense.Infrastructure;

/// <summary>The Expense module's tables, in the <c>exp</c> schema.</summary>
internal sealed class ExpenseModel : IModuleModel
{
    public const string Schema = "exp";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ExpenseCategory>(entity =>
        {
            entity.ToTable("Category", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.LimitPerLine).HasPrecision(18, 2);
            entity.Property(row => row.ReceiptRequiredAbove).HasPrecision(18, 2);
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_Category_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ExpenseClaim>(entity =>
        {
            entity.ToTable("Claim", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Currency).HasMaxLength(3);
            entity.Property(row => row.Total).HasPrecision(18, 2);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.PaymentMode).HasConversion<byte?>();
            entity.Property(row => row.PaymentReference).HasMaxLength(100);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Claim_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.Status }).HasDatabaseName("IX_Claim_TenantId_Employee_Status");
            entity.HasIndex(row => new { row.TenantId, row.TravelRequestId }).HasDatabaseName("IX_Claim_TenantId_TravelRequest");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ExpenseLine>(entity =>
        {
            entity.ToTable("Line", Schema);
            entity.Property(row => row.Description).HasMaxLength(300);
            entity.Property(row => row.Amount).HasPrecision(18, 2);
            entity.Property(row => row.Merchant).HasMaxLength(200);
            entity.Property(row => row.PolicyFlag).HasMaxLength(200);
            entity.Property(row => row.Justification).HasMaxLength(500);
            entity.HasOne<ExpenseClaim>().WithMany().HasForeignKey(row => row.ClaimId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExpenseCategory>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.ClaimId }).HasDatabaseName("IX_Line_TenantId_Claim");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<TravelRequest>(entity =>
        {
            entity.ToTable("TravelRequest", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.Purpose).HasMaxLength(500);
            entity.Property(row => row.FromCity).HasMaxLength(100);
            entity.Property(row => row.ToCity).HasMaxLength(100);
            entity.Property(row => row.Mode).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.EstimatedCost).HasPrecision(18, 2);
            entity.Property(row => row.AdvanceRequested).HasPrecision(18, 2);
            entity.Property(row => row.AdvancePaid).HasPrecision(18, 2);
            entity.Property(row => row.SettlementAmount).HasPrecision(18, 2);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_TravelRequest_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_TravelRequest_TenantId_Employee");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class ExpenseData
{
    extension(AppDbContext db)
    {
        public DbSet<ExpenseCategory> ExpenseCategories => db.Set<ExpenseCategory>();

        public DbSet<ExpenseClaim> ExpenseClaims => db.Set<ExpenseClaim>();

        public DbSet<ExpenseLine> ExpenseLines => db.Set<ExpenseLine>();

        public DbSet<TravelRequest> TravelRequests => db.Set<TravelRequest>();
    }
}
