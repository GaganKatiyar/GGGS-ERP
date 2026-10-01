using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Payroll.Infrastructure;

/// <summary>Payroll's database: salary components, structures and their component lines, employee
/// salary assignments and their computed component breakdowns, pay groups/periods/runs and their
/// per-employee results, and the employee snapshot fed from Core HR.</summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "pay";

    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();

    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();

    public DbSet<SalaryStructureComponent> SalaryStructureComponents => Set<SalaryStructureComponent>();

    public DbSet<EmployeeSalary> EmployeeSalaries => Set<EmployeeSalary>();

    public DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents => Set<EmployeeSalaryComponent>();

    public DbSet<PayGroup> PayGroups => Set<PayGroup>();

    public DbSet<PayPeriod> PayPeriods => Set<PayPeriod>();

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();

    public DbSet<PayrollRunEmployee> PayrollRunEmployees => Set<PayrollRunEmployee>();

    public DbSet<PayrollRunLine> PayrollRunLines => Set<PayrollRunLine>();

    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SalaryComponent>(entity =>
        {
            entity.ToTable("SalaryComponent", "pay");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Kind).HasConversion<byte>();
            entity.Property(x => x.CalcType).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_SalaryComponent_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<SalaryStructure>(entity =>
        {
            entity.ToTable("SalaryStructure", "pay");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_SalaryStructure_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<SalaryStructureComponent>(entity =>
        {
            entity.ToTable("SalaryStructureComponent", "pay");
            entity.Property(x => x.Value).HasColumnType("decimal(12,2)");

            entity.HasIndex(x => new { x.TenantId, x.StructureId, x.ComponentId })
                .HasDatabaseName("UX_SalaryStructureComponent_TenantId_StructureId_ComponentId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSalary>(entity =>
        {
            entity.ToTable("EmployeeSalary", "pay");
            entity.Property(x => x.AnnualCtc).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom })
                .HasDatabaseName("IX_EmployeeSalary_TenantId_EmployeeId_EffectiveFrom");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSalaryComponent>(entity =>
        {
            entity.ToTable("EmployeeSalaryComponent", "pay");
            entity.Property(x => x.MonthlyAmount).HasColumnType("decimal(14,2)");
            entity.Property(x => x.AnnualAmount).HasColumnType("decimal(16,2)");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeSalaryId })
                .HasDatabaseName("IX_EmployeeSalaryComponent_TenantId_EmployeeSalaryId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayGroup>(entity =>
        {
            entity.ToTable("PayGroup", "pay");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Frequency).HasConversion<byte>();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_PayGroup_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayPeriod>(entity =>
        {
            entity.ToTable("PayPeriod", "pay");
            entity.Property(x => x.PeriodCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.PayGroupId, x.PeriodCode })
                .HasDatabaseName("UX_PayPeriod_TenantId_PayGroupId_PeriodCode")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayrollRun>(entity =>
        {
            entity.ToTable("PayrollRun", "pay");
            entity.Property(x => x.RunNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.GrossTotal).HasColumnType("decimal(18,2)");
            entity.Property(x => x.NetTotal).HasColumnType("decimal(18,2)");

            entity.HasIndex(x => new { x.TenantId, x.RunNo })
                .HasDatabaseName("UX_PayrollRun_TenantId_RunNo")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.PayPeriodId })
                .HasDatabaseName("IX_PayrollRun_TenantId_PayPeriodId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayrollRunEmployee>(entity =>
        {
            entity.ToTable("PayrollRunEmployee", "pay");
            entity.Property(x => x.Gross).HasColumnType("decimal(14,2)");
            entity.Property(x => x.NetPay).HasColumnType("decimal(14,2)");

            entity.HasIndex(x => new { x.TenantId, x.PayrollRunId })
                .HasDatabaseName("IX_PayrollRunEmployee_TenantId_PayrollRunId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayrollRunLine>(entity =>
        {
            entity.ToTable("PayrollRunLine", "pay");
            entity.Property(x => x.Amount).HasColumnType("decimal(14,2)");

            entity.HasIndex(x => new { x.TenantId, x.PayrollRunEmployeeId })
                .HasDatabaseName("IX_PayrollRunLine_TenantId_PayrollRunEmployeeId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSnapshot>(entity =>
        {
            entity.ToTable("EmployeeSnapshot", "ref");
            entity.Property(x => x.EmployeeCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("UX_EmployeeSnapshot_TenantId_EmployeeId")
                .IsUnique();

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
