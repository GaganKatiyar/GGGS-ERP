using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Infrastructure;

/// <summary>
/// The Core HR database: organization structure and employee records.
/// <para>
/// Every table here is tenant-owned. The default schema is <c>org</c>; employee tables declare
/// <c>emp</c> explicitly, the same way every other multi-module service schema-separates its tables
/// while sharing one database and one migration history.
/// </para>
/// </summary>
public sealed class CoreHrDbContext(DbContextOptions<CoreHrDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    /// <summary>The employee tables' own schema, distinct from organization's default.</summary>
    public const string EmployeeSchema = "emp";

    protected override string DefaultSchema => "org";

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Designation> Designations => Set<Designation>();

    public DbSet<Grade> Grades => Set<Grade>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmploymentRecord> EmploymentRecords => Set<EmploymentRecord>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LegalEntity>(entity =>
        {
            entity.ToTable("LegalEntity");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RegistrationNumber).HasMaxLength(100);
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.CompliancePackCode).HasMaxLength(50);

            entity.OwnsOne(x => x.Address, address =>
            {
                address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
                address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
                address.Property(a => a.City).HasColumnName("City").HasMaxLength(100);
                address.Property(a => a.StateCode).HasColumnName("StateCode").HasMaxLength(10);
                address.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
                address.Property(a => a.CountryCode).HasColumnName("AddressCountryCode").HasMaxLength(2);
            });

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_LegalEntity_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<BusinessUnit>(entity =>
        {
            entity.ToTable("BusinessUnit");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_BusinessUnit_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("Branch");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.StateCode).HasMaxLength(10);
            entity.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();

            entity.OwnsOne(x => x.Address, address =>
            {
                address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
                address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
                address.Property(a => a.City).HasColumnName("City").HasMaxLength(100);
                address.Property(a => a.StateCode).HasColumnName("AddressStateCode").HasMaxLength(10);
                address.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
                address.Property(a => a.CountryCode).HasColumnName("AddressCountryCode").HasMaxLength(2);
            });

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Branch_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // A same-tenant composite FK: the database itself refuses a branch that names another
            // tenant's legal entity, rather than relying only on the query filter and application
            // code to keep the two apart (§9, layer 4 of the isolation model).
            entity.HasOne<LegalEntity>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Location");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LocationType).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Location_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.BranchId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Department");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Department_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.ParentDepartmentId })
                .HasDatabaseName("IX_Department_TenantId_ParentDepartmentId");

            // Self-referencing: a department's parent must be a department in the same tenant. SQL
            // Server allows a table to reference itself this way as long as the relationship does
            // not cascade delete, which it must not - deleting a department is a soft delete, never
            // a cascade that could take a whole subtree with it.
            entity.HasOne<Department>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ParentDepartmentId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<CostCenter>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.CostCenterId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<BusinessUnit>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.BusinessUnitId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Designation>(entity =>
        {
            entity.ToTable("Designation");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.JobFamily).HasMaxLength(100);

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Designation_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Grade>(entity =>
        {
            entity.ToTable("Grade");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.SalaryBandMinimum).HasPrecision(18, 2);
            entity.Property(x => x.SalaryBandMaximum).HasPrecision(18, 2);

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Grade_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<CostCenter>(entity =>
        {
            entity.ToTable("CostCenter");
            entity.Property(x => x.Code).HasMaxLength(OrganizationCode.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.GlAccountCode).HasMaxLength(50);

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_CostCenter_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.ParentCostCenterId })
                .HasDatabaseName("IX_CostCenter_TenantId_ParentCostCenterId");

            entity.HasOne<CostCenter>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ParentCostCenterId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("Employee", EmployeeSchema);
            entity.Property(x => x.EmployeeCode).HasMaxLength(EmployeeCodeFormat.MaxLength).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.MiddleName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.DisplayName).HasMaxLength(303).IsRequired(); // three 100-char name parts plus two separating spaces
            entity.Property(x => x.WorkEmail).HasMaxLength(256);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeCode })
                .HasDatabaseName("UX_Employee_TenantId_EmployeeCode")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // Not filtered to IsDeleted = 0 like the others: a work email is released back to the
            // pool the moment someone leaves, filtered or not, because two live employees sharing
            // an inbox is the failure this index exists to prevent, and "live" already means
            // IsDeleted = 0 is irrelevant here - nothing in this module soft-deletes an Employee row
            // at all, only Employee.Exit ever changes what it means to be current.
            entity.HasIndex(x => new { x.TenantId, x.WorkEmail })
                .HasDatabaseName("UX_Employee_TenantId_WorkEmail")
                .IsUnique()
                .HasFilter("[WorkEmail] IS NOT NULL AND [IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.UserId })
                .HasDatabaseName("UX_Employee_TenantId_UserId")
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL AND [IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmploymentRecord>(entity =>
        {
            entity.ToTable("EmploymentRecord", EmployeeSchema);
            entity.Property(x => x.EmploymentType).HasConversion<byte>();
            entity.Property(x => x.ChangeType).HasConversion<byte>();
            entity.Property(x => x.ChangeReason).HasMaxLength(500);

            // Exactly one current record per employee - the query filter's IsDeleted = 0 combines
            // with this to mean "the employee's current assignment has exactly one answer," which
            // is the invariant everything downstream (payroll inputs, attendance rosters) depends on.
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("UX_EmploymentRecord_TenantId_EmployeeId_Current")
                .IsUnique()
                .HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom })
                .HasDatabaseName("IX_EmploymentRecord_TenantId_EmployeeId_Timeline");

            entity.HasIndex(x => new { x.TenantId, x.DepartmentId })
                .HasDatabaseName("IX_EmploymentRecord_TenantId_DepartmentId")
                .HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.ReportingManagerId })
                .HasDatabaseName("IX_EmploymentRecord_TenantId_ReportingManagerId")
                .HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

            entity.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.EmployeeId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            // The manager is also an Employee row, in the same table this record itself points at
            // with EmployeeId - EF needs a second, independently named relationship to the same
            // principal so the two do not collide.
            entity.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportingManagerId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<LegalEntity>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.LegalEntityId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.BranchId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Department>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Designation>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.DesignationId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Grade>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.GradeId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<CostCenter>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.CostCenterId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
