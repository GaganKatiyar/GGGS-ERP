using Gggs.Data;
using Gggs.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gggs.Modules.Organization.Infrastructure;

/// <summary>The Organization module's tables, in the <c>org</c> schema.</summary>
internal sealed class OrganizationModel : IModuleModel
{
    public const string Schema = "org";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LegalEntity>(entity =>
        {
            ConfigureUnit(entity, "LegalEntity");
            entity.Property(unit => unit.LegalName).HasMaxLength(300);
            entity.Property(unit => unit.CountryCode).HasMaxLength(8).IsRequired();
            entity.Property(unit => unit.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(unit => unit.Gstin).HasMaxLength(15);
            entity.Property(unit => unit.Pan).HasMaxLength(10);
            entity.Property(unit => unit.Cin).HasMaxLength(21);
            entity.Property(unit => unit.Tan).HasMaxLength(10);
            entity.OwnsOne(unit => unit.Address, ConfigureAddress);
        });

        modelBuilder.Entity<BusinessUnit>(entity =>
        {
            ConfigureUnit(entity, "BusinessUnit");
            entity.HasOne<LegalEntity>().WithMany().HasForeignKey(unit => unit.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Division>(entity =>
        {
            ConfigureUnit(entity, "Division");
            entity.HasOne<BusinessUnit>().WithMany().HasForeignKey(unit => unit.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CostCenter>(entity => ConfigureUnit(entity, "CostCenter"));
        modelBuilder.Entity<ProfitCenter>(entity => ConfigureUnit(entity, "ProfitCenter"));

        modelBuilder.Entity<Location>(entity =>
        {
            ConfigureUnit(entity, "Location");
            entity.Property(unit => unit.TimeZone).HasMaxLength(64);
            entity.OwnsOne(unit => unit.Address, ConfigureAddress);
            entity.HasOne<LegalEntity>().WithMany().HasForeignKey(unit => unit.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Grade>(entity =>
        {
            ConfigureUnit(entity, "Grade");
            entity.HasIndex(unit => new { unit.TenantId, unit.Rank }).HasDatabaseName("IX_Grade_TenantId_Rank");
        });

        modelBuilder.Entity<JobLevel>(entity =>
        {
            ConfigureUnit(entity, "JobLevel");
            entity.HasIndex(unit => new { unit.TenantId, unit.Rank }).HasDatabaseName("IX_JobLevel_TenantId_Rank");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            ConfigureUnit(entity, "Department");
            entity.HasOne<Department>().WithMany().HasForeignKey(unit => unit.ParentDepartmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Division>().WithMany().HasForeignKey(unit => unit.DivisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CostCenter>().WithMany().HasForeignKey(unit => unit.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProfitCenter>().WithMany().HasForeignKey(unit => unit.ProfitCenterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(unit => unit.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(unit => new { unit.TenantId, unit.ParentDepartmentId }).HasDatabaseName("IX_Department_TenantId_Parent");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            ConfigureUnit(entity, "Job");
            entity.Property(unit => unit.JobFamilyCode).HasMaxLength(64);
            entity.Property(unit => unit.Description).HasMaxLength(4000);
            entity.Property(unit => unit.MinimumExperienceYears).HasPrecision(4, 1);
            entity.Property(unit => unit.MaximumExperienceYears).HasPrecision(4, 1);

            // A JSON array until the Skills module (M6) gives skills identities.
            entity.PrimitiveCollection(unit => unit.RequiredSkills)
                .HasColumnName("RequiredSkills")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasOne<JobLevel>().WithMany().HasForeignKey(unit => unit.JobLevelId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(unit => unit.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Position>(entity =>
        {
            ConfigureUnit(entity, "Position");
            entity.HasOne<Job>().WithMany().HasForeignKey(unit => unit.JobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(unit => unit.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(unit => unit.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Grade>().WithMany().HasForeignKey(unit => unit.GradeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Position>().WithMany().HasForeignKey(unit => unit.ReportsToPositionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(unit => new { unit.TenantId, unit.DepartmentId }).HasDatabaseName("IX_Position_TenantId_DepartmentId");
        });
    }

    private static void ConfigureUnit<T>(EntityTypeBuilder<T> entity, string table)
        where T : OrgUnit
    {
        entity.ToTable(table, Schema);
        entity.Property(unit => unit.Code).HasMaxLength(32).IsRequired();
        entity.Property(unit => unit.Name).HasMaxLength(200).IsRequired();

        // Unique per tenant among live rows; a deleted code can be reused.
        entity.HasIndex(unit => new { unit.TenantId, unit.Code })
            .HasDatabaseName($"UX_{table}_TenantId_Code")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        entity.Ignore(unit => unit.DomainEvents);
    }

    private static void ConfigureAddress<TOwner>(OwnedNavigationBuilder<TOwner, PostalAddress> address)
        where TOwner : class
    {
        address.Property(value => value.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
        address.Property(value => value.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
        address.Property(value => value.City).HasColumnName("City").HasMaxLength(100);
        address.Property(value => value.StateCode).HasColumnName("StateCode").HasMaxLength(64);
        address.Property(value => value.CountryCode).HasColumnName("AddressCountryCode").HasMaxLength(8);
        address.Property(value => value.PostalCode).HasColumnName("PostalCode").HasMaxLength(16);
    }
}

internal static class OrganizationData
{
    extension(AppDbContext db)
    {
        public DbSet<LegalEntity> LegalEntities => db.Set<LegalEntity>();

        public DbSet<BusinessUnit> BusinessUnits => db.Set<BusinessUnit>();

        public DbSet<Division> Divisions => db.Set<Division>();

        public DbSet<Department> Departments => db.Set<Department>();

        public DbSet<Location> Locations => db.Set<Location>();

        public DbSet<CostCenter> CostCenters => db.Set<CostCenter>();

        public DbSet<ProfitCenter> ProfitCenters => db.Set<ProfitCenter>();

        public DbSet<Grade> Grades => db.Set<Grade>();

        public DbSet<JobLevel> JobLevels => db.Set<JobLevel>();

        public DbSet<Job> Jobs => db.Set<Job>();

        public DbSet<Position> Positions => db.Set<Position>();
    }
}
