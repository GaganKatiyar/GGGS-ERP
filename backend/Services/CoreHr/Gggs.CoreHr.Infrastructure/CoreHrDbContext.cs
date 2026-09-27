using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Infrastructure;

/// <summary>
/// The Core HR database: organization structure today, employee records once that module lands.
/// <para>
/// Every table here is tenant-owned. The default schema is <c>org</c>; employee tables will declare
/// <c>emp</c> explicitly when they arrive, the same way every other multi-module service schema-
/// separates its tables while sharing one database and one migration history.
/// </para>
/// </summary>
public sealed class CoreHrDbContext(DbContextOptions<CoreHrDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "org";

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Designation> Designations => Set<Designation>();

    public DbSet<Grade> Grades => Set<Grade>();

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
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
