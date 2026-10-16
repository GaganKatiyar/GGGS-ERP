using Gggs.Data;
using Gggs.Modules.Payroll.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Infrastructure;

/// <summary>The Payroll module's tables, in the <c>pay</c> schema.</summary>
internal sealed class PayrollModel : IModuleModel
{
    public const string Schema = "pay";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SalaryComponent>(entity =>
        {
            entity.ToTable("SalaryComponent", Schema);
            entity.Property(component => component.Code).HasMaxLength(30);
            entity.Property(component => component.Name).HasMaxLength(100);
            entity.Property(component => component.Kind).HasConversion<byte>();
            entity.Property(component => component.CalculationType).HasConversion<byte>();
            entity.Property(component => component.InputKind).HasConversion<byte?>();
            entity.Property(component => component.Value).HasPrecision(18, 4);
            entity.Property(component => component.PercentOf).HasMaxLength(30);
            entity.Property(component => component.Formula).HasMaxLength(500);
            entity.Property(component => component.Description).HasMaxLength(500);
            entity.HasIndex(component => new { component.TenantId, component.Code })
                .HasDatabaseName("UX_SalaryComponent_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(component => component.DomainEvents);
        });

        modelBuilder.Entity<SalaryStructure>(entity =>
        {
            entity.ToTable("SalaryStructure", Schema);
            entity.Property(structure => structure.Code).HasMaxLength(30);
            entity.Property(structure => structure.Name).HasMaxLength(100);
            entity.Property(structure => structure.Description).HasMaxLength(500);
            entity.Property(structure => structure.LinesJson).IsRequired();
            entity.HasIndex(structure => new { structure.TenantId, structure.Code })
                .HasDatabaseName("UX_SalaryStructure_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(structure => structure.Lines);
            entity.Ignore(structure => structure.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSalary>(entity =>
        {
            entity.ToTable("EmployeeSalary", Schema);
            entity.Property(salary => salary.AnnualCtc).HasPrecision(18, 2);
            entity.Property(salary => salary.Currency).HasMaxLength(3);
            entity.Property(salary => salary.Reason).HasConversion<byte>();
            entity.Property(salary => salary.Source).HasMaxLength(40);
            entity.Property(salary => salary.Notes).HasMaxLength(500);
            entity.HasOne<SalaryStructure>().WithMany().HasForeignKey(salary => salary.StructureId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(salary => new { salary.TenantId, salary.EmployeeId, salary.EffectiveFrom }).HasDatabaseName("IX_EmployeeSalary_TenantId_Employee_From");
            entity.Ignore(salary => salary.DomainEvents);
        });

        modelBuilder.Entity<PayrollSettings>(entity =>
        {
            entity.ToTable("PayrollSettings", Schema);
            entity.Property(settings => settings.ProrationBasis).HasConversion<byte>();
            entity.Property(settings => settings.DefaultRegime).HasConversion<byte>();
            entity.Property(settings => settings.OvertimeMultiplier).HasPrecision(4, 2);
            entity.Property(settings => settings.StandardHoursPerDay).HasPrecision(4, 2);
            entity.Property(settings => settings.DefaultStateCode).HasMaxLength(16);
            entity.Property(settings => settings.DefaultCurrency).HasMaxLength(3);
            entity.Property(settings => settings.EmployerName).HasMaxLength(200);
            entity.Property(settings => settings.EmployerAddress).HasMaxLength(500);
            entity.Property(settings => settings.EmployerPan).HasMaxLength(10);
            entity.Property(settings => settings.EmployerTan).HasMaxLength(10);
            entity.Property(settings => settings.PfEstablishmentCode).HasMaxLength(30);
            entity.Property(settings => settings.EsiEmployerCode).HasMaxLength(30);
            entity.HasIndex(settings => settings.TenantId)
                .HasDatabaseName("UX_PayrollSettings_TenantId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(settings => settings.DomainEvents);
        });

        modelBuilder.Entity<PayrollInput>(entity =>
        {
            entity.ToTable("PayrollInput", Schema);
            entity.Property(input => input.Kind).HasConversion<byte>();
            entity.Property(input => input.Status).HasConversion<byte>();
            entity.Property(input => input.Amount).HasPrecision(18, 2);
            entity.Property(input => input.Quantity).HasPrecision(9, 2);
            entity.Property(input => input.Description).HasMaxLength(200);
            entity.Property(input => input.Source).HasMaxLength(40);
            entity.HasOne<SalaryComponent>().WithMany().HasForeignKey(input => input.ComponentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(input => new { input.TenantId, input.Period, input.EmployeeId }).HasDatabaseName("IX_PayrollInput_TenantId_Period_Employee");
            entity.HasIndex(input => new { input.TenantId, input.Source, input.SourceId }).HasDatabaseName("IX_PayrollInput_TenantId_Source");
            entity.Ignore(input => input.DomainEvents);
        });

        modelBuilder.Entity<TaxDeclaration>(entity =>
        {
            entity.ToTable("TaxDeclaration", Schema);
            entity.Property(declaration => declaration.Regime).HasConversion<byte>();
            entity.Property(declaration => declaration.Status).HasConversion<byte>();
            entity.Property(declaration => declaration.ItemsJson).IsRequired();
            entity.Property(declaration => declaration.MonthlyRent).HasPrecision(18, 2);
            entity.Property(declaration => declaration.LandlordPan).HasMaxLength(10);
            entity.Property(declaration => declaration.OtherIncome).HasPrecision(18, 2);
            entity.Property(declaration => declaration.PreviousEmployerIncome).HasPrecision(18, 2);
            entity.Property(declaration => declaration.PreviousEmployerTds).HasPrecision(18, 2);
            entity.Property(declaration => declaration.VerifierNotes).HasMaxLength(1000);
            entity.HasIndex(declaration => new { declaration.TenantId, declaration.EmployeeId, declaration.FinancialYear })
                .HasDatabaseName("UX_TaxDeclaration_TenantId_Employee_Year")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(declaration => declaration.Items);
            entity.Ignore(declaration => declaration.DomainEvents);
        });

        modelBuilder.Entity<PayrollRun>(entity =>
        {
            entity.ToTable("PayrollRun", Schema);
            entity.Property(run => run.Status).HasConversion<byte>();
            entity.Property(run => run.Currency).HasMaxLength(3);
            entity.Property(run => run.Gross).HasPrecision(18, 2);
            entity.Property(run => run.Deductions).HasPrecision(18, 2);
            entity.Property(run => run.Net).HasPrecision(18, 2);
            entity.Property(run => run.EmployerCost).HasPrecision(18, 2);
            entity.Property(run => run.PaymentReference).HasMaxLength(100);
            entity.Property(run => run.Notes).HasMaxLength(1000);
            entity.HasIndex(run => new { run.TenantId, run.Period }).HasDatabaseName("IX_PayrollRun_TenantId_Period");
            entity.Ignore(run => run.IsLocked);
            entity.Ignore(run => run.DomainEvents);
        });

        modelBuilder.Entity<PayrollRunItem>(entity =>
        {
            entity.ToTable("PayrollRunItem", Schema);
            entity.Property(item => item.EmployeeCode).HasMaxLength(40);
            entity.Property(item => item.EmployeeName).HasMaxLength(200);
            entity.Property(item => item.Designation).HasMaxLength(200);
            entity.Property(item => item.Status).HasConversion<byte>();
            entity.Property(item => item.PeriodDays).HasPrecision(5, 2);
            entity.Property(item => item.PaidDays).HasPrecision(5, 2);
            entity.Property(item => item.LopDays).HasPrecision(5, 2);
            entity.Property(item => item.AnnualCtc).HasPrecision(18, 2);
            entity.Property(item => item.Gross).HasPrecision(18, 2);
            entity.Property(item => item.Reimbursements).HasPrecision(18, 2);
            entity.Property(item => item.Deductions).HasPrecision(18, 2);
            entity.Property(item => item.Net).HasPrecision(18, 2);
            entity.Property(item => item.EmployerContributions).HasPrecision(18, 2);
            entity.Property(item => item.TaxableEarnings).HasPrecision(18, 2);
            entity.Property(item => item.LinesJson).IsRequired();
            entity.Property(item => item.RulesJson).IsRequired();
            entity.Property(item => item.IssuesJson).IsRequired();
            entity.Property(item => item.StateCode).HasMaxLength(16);
            entity.Property(item => item.HoldReason).HasMaxLength(300);
            entity.HasOne<PayrollRun>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.RunId, item.EmployeeId }).HasDatabaseName("IX_PayrollRunItem_TenantId_Run_Employee");
            entity.HasIndex(item => new { item.TenantId, item.EmployeeId }).HasDatabaseName("IX_PayrollRunItem_TenantId_Employee");
            entity.Ignore(item => item.Lines);
            entity.Ignore(item => item.Rules);
            entity.Ignore(item => item.Issues);
            entity.Ignore(item => item.Tax);
            entity.Ignore(item => item.ProvidentFund);
            entity.Ignore(item => item.IsPayable);
            entity.Ignore(item => item.DomainEvents);
        });

        modelBuilder.Entity<Payslip>(entity =>
        {
            entity.ToTable("Payslip", Schema);
            entity.Property(payslip => payslip.Currency).HasMaxLength(3);
            entity.Property(payslip => payslip.Gross).HasPrecision(18, 2);
            entity.Property(payslip => payslip.Deductions).HasPrecision(18, 2);
            entity.Property(payslip => payslip.Net).HasPrecision(18, 2);
            entity.Property(payslip => payslip.DocumentJson).IsRequired();
            entity.HasOne<PayrollRun>().WithMany().HasForeignKey(payslip => payslip.RunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payslip => new { payslip.TenantId, payslip.EmployeeId, payslip.Period }).HasDatabaseName("IX_Payslip_TenantId_Employee_Period");
            entity.HasIndex(payslip => new { payslip.TenantId, payslip.RunId }).HasDatabaseName("IX_Payslip_TenantId_Run");
            entity.Ignore(payslip => payslip.DomainEvents);
        });
    }
}

internal static class PayrollData
{
    extension(AppDbContext db)
    {
        public DbSet<SalaryComponent> SalaryComponents => db.Set<SalaryComponent>();

        public DbSet<SalaryStructure> SalaryStructures => db.Set<SalaryStructure>();

        public DbSet<EmployeeSalary> EmployeeSalaries => db.Set<EmployeeSalary>();

        public DbSet<PayrollSettings> PayrollSettings => db.Set<PayrollSettings>();

        public DbSet<PayrollInput> PayrollInputs => db.Set<PayrollInput>();

        public DbSet<TaxDeclaration> TaxDeclarations => db.Set<TaxDeclaration>();

        public DbSet<PayrollRun> PayrollRuns => db.Set<PayrollRun>();

        public DbSet<PayrollRunItem> PayrollRunItems => db.Set<PayrollRunItem>();

        public DbSet<Payslip> Payslips => db.Set<Payslip>();
    }
}
