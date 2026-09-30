using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Payroll.Infrastructure;

/// <summary>Payroll's database: salary components, structures and their component lines, employee
/// salary assignments and their computed component breakdowns, and the employee snapshot fed from
/// Core HR.</summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "pay";

    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();

    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();

    public DbSet<SalaryStructureComponent> SalaryStructureComponents => Set<SalaryStructureComponent>();

    public DbSet<EmployeeSalary> EmployeeSalaries => Set<EmployeeSalary>();

    public DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents => Set<EmployeeSalaryComponent>();

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
