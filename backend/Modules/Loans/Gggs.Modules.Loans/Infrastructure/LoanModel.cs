using Gggs.Data;
using Gggs.Modules.Loans.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Loans.Infrastructure;

/// <summary>The Loans module's tables, in the <c>loan</c> schema.</summary>
internal sealed class LoanModel : IModuleModel
{
    public const string Schema = "loan";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LoanType>(entity =>
        {
            entity.ToTable("LoanType", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Description).HasMaxLength(500);
            entity.Property(row => row.InterestRate).HasPrecision(5, 2);
            entity.Property(row => row.MaxAmount).HasPrecision(18, 2);
            entity.Property(row => row.MaxGrossMultiple).HasPrecision(6, 2);
            entity.Property(row => row.MaxEmiPercentOfGross).HasPrecision(5, 2);
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_LoanType_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.ToTable("Loan", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.Principal).HasPrecision(18, 2);
            entity.Property(row => row.InterestRate).HasPrecision(5, 2);
            entity.Property(row => row.Emi).HasPrecision(18, 2);
            entity.Property(row => row.Purpose).HasMaxLength(500);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.DecisionNote).HasMaxLength(500);
            entity.Property(row => row.DisbursementMode).HasConversion<byte?>();
            entity.Property(row => row.DisbursementReference).HasMaxLength(100);
            entity.Property(row => row.Outstanding).HasPrecision(18, 2);
            entity.Property(row => row.CloseNote).HasMaxLength(200);
            entity.Ignore(row => row.IsOpen);
            entity.HasOne<LoanType>().WithMany().HasForeignKey(row => row.LoanTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Loan_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.Status }).HasDatabaseName("IX_Loan_TenantId_Employee_Status");
            entity.HasIndex(row => new { row.TenantId, row.SettlementId }).HasDatabaseName("IX_Loan_TenantId_Settlement");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<LoanInstalment>(entity =>
        {
            entity.ToTable("Instalment", Schema);
            entity.Property(row => row.Principal).HasPrecision(18, 2);
            entity.Property(row => row.Interest).HasPrecision(18, 2);
            entity.Property(row => row.Amount).HasPrecision(18, 2);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasOne<Loan>().WithMany().HasForeignKey(row => row.LoanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.LoanId, row.Sequence }).HasDatabaseName("IX_Instalment_TenantId_Loan_Sequence");
            entity.HasIndex(row => new { row.TenantId, row.Status, row.Month }).HasDatabaseName("IX_Instalment_TenantId_Status_Month");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<LoanRepayment>(entity =>
        {
            entity.ToTable("Repayment", Schema);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.Amount).HasPrecision(18, 2);
            entity.Property(row => row.Reference).HasMaxLength(100);
            entity.Property(row => row.Note).HasMaxLength(300);
            entity.HasOne<Loan>().WithMany().HasForeignKey(row => row.LoanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.LoanId }).HasDatabaseName("IX_Repayment_TenantId_Loan");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class LoanData
{
    extension(AppDbContext db)
    {
        public DbSet<LoanType> LoanTypes => db.Set<LoanType>();

        public DbSet<Loan> Loans => db.Set<Loan>();

        public DbSet<LoanInstalment> LoanInstalments => db.Set<LoanInstalment>();

        public DbSet<LoanRepayment> LoanRepayments => db.Set<LoanRepayment>();
    }
}
