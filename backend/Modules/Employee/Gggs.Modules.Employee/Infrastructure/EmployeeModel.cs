using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>The Employee module's tables, in the <c>emp</c> schema.</summary>
internal sealed class EmployeeModel : IModuleModel
{
    public const string Schema = "emp";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<EmployeeEntity>(entity =>
        {
            entity.ToTable("Employee", Schema);

            entity.Property(employee => employee.EmployeeCode).HasMaxLength(32).IsRequired();
            entity.Property(employee => employee.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(employee => employee.MiddleName).HasMaxLength(100);
            entity.Property(employee => employee.LastName).HasMaxLength(100);
            entity.Property(employee => employee.PreferredName).HasMaxLength(100);
            entity.Property(employee => employee.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(employee => employee.GenderCode).HasMaxLength(64);
            entity.Property(employee => employee.MaritalStatusCode).HasMaxLength(64);
            entity.Property(employee => employee.NationalityCode).HasMaxLength(64);
            entity.Property(employee => employee.BloodGroupCode).HasMaxLength(64);
            entity.Property(employee => employee.PersonalEmail).HasMaxLength(256);
            entity.Property(employee => employee.OfficialEmail).HasMaxLength(256);
            entity.Property(employee => employee.MobilePhone).HasMaxLength(32);
            entity.Property(employee => employee.AlternatePhone).HasMaxLength(32);
            entity.Property(employee => employee.Status).HasConversion<byte>();
            entity.Property(employee => employee.ExitReasonCode).HasMaxLength(64);
            entity.Property(employee => employee.EmploymentTypeCode).HasMaxLength(64).IsRequired();
            entity.Property(employee => employee.WorkModeCode).HasMaxLength(64).IsRequired();
            entity.Property(employee => employee.Designation).HasMaxLength(200);
            entity.Property(employee => employee.ProfessionalTaxStateCode).HasMaxLength(64);
            entity.Property(employee => employee.BankName).HasMaxLength(200);
            entity.Property(employee => employee.BankIfsc).HasMaxLength(11);
            entity.Property(employee => employee.BankAccountHolder).HasMaxLength(200);

            entity.OwnsOne(employee => employee.CurrentAddress, address => ConfigureAddress(address, "Current"));
            entity.OwnsOne(employee => employee.PermanentAddress, address => ConfigureAddress(address, "Permanent"));

            ConfigureProtected(entity, employee => employee.Pan, "Pan");
            ConfigureProtected(entity, employee => employee.Uan, "Uan");
            ConfigureProtected(entity, employee => employee.EsicNumber, "Esic");
            ConfigureProtected(entity, employee => employee.NationalId, "NationalId");
            ConfigureProtected(entity, employee => employee.BankAccountNumber, "BankAccount");

            entity.HasMany(employee => employee.EmploymentRecords).WithOne().HasForeignKey(record => record.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(employee => employee.Dependents).WithOne().HasForeignKey(dependent => dependent.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(employee => employee.EmergencyContacts).WithOne().HasForeignKey(contact => contact.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(employee => employee.EmploymentRecords).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(employee => employee.Dependents).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(employee => employee.EmergencyContacts).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasIndex(employee => new { employee.TenantId, employee.EmployeeCode })
                .HasDatabaseName("UX_Employee_TenantId_EmployeeCode")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(employee => new { employee.TenantId, employee.OfficialEmail })
                .HasDatabaseName("UX_Employee_TenantId_OfficialEmail")
                .IsUnique()
                .HasFilter("[OfficialEmail] IS NOT NULL AND [IsDeleted] = 0");

            // The directory's filters: by department, by manager, by position (occupancy), by status.
            entity.HasIndex(employee => new { employee.TenantId, employee.DepartmentId, employee.Status }).HasDatabaseName("IX_Employee_TenantId_DepartmentId");
            entity.HasIndex(employee => new { employee.TenantId, employee.ManagerEmployeeId }).HasDatabaseName("IX_Employee_TenantId_ManagerEmployeeId");
            entity.HasIndex(employee => new { employee.TenantId, employee.PositionId }).HasDatabaseName("IX_Employee_TenantId_PositionId");
            entity.HasIndex(employee => new { employee.TenantId, employee.DisplayName }).HasDatabaseName("IX_Employee_TenantId_DisplayName");

            entity.Ignore(employee => employee.CurrentRecord);
            entity.Ignore(employee => employee.IsConfirmed);
            entity.Ignore(employee => employee.DomainEvents);
        });

        modelBuilder.Entity<EmploymentRecord>(entity =>
        {
            entity.ToTable("EmploymentRecord", Schema);
            entity.Property(record => record.ChangeType).HasConversion<byte>();
            entity.Property(record => record.EmploymentTypeCode).HasMaxLength(64).IsRequired();
            entity.Property(record => record.WorkModeCode).HasMaxLength(64).IsRequired();
            entity.Property(record => record.Designation).HasMaxLength(200);
            entity.Property(record => record.Reason).HasMaxLength(500);
            entity.HasIndex(record => new { record.TenantId, record.EmployeeId, record.EffectiveFrom }).HasDatabaseName("IX_EmploymentRecord_TenantId_EmployeeId_EffectiveFrom");

            // One open record per employee: the current assignment is never ambiguous.
            entity.HasIndex(record => new { record.TenantId, record.EmployeeId })
                .HasDatabaseName("UX_EmploymentRecord_TenantId_EmployeeId_Open")
                .IsUnique()
                .HasFilter("[EffectiveTo] IS NULL AND [IsDeleted] = 0");

            entity.Ignore(record => record.DomainEvents);
        });

        modelBuilder.Entity<EmployeeDependent>(entity =>
        {
            entity.ToTable("EmployeeDependent", Schema);
            entity.Property(dependent => dependent.FullName).HasMaxLength(200).IsRequired();
            entity.Property(dependent => dependent.RelationshipCode).HasMaxLength(64).IsRequired();
            entity.Property(dependent => dependent.GenderCode).HasMaxLength(64);
            entity.HasIndex(dependent => new { dependent.TenantId, dependent.EmployeeId }).HasDatabaseName("IX_EmployeeDependent_TenantId_EmployeeId");
            entity.Ignore(dependent => dependent.DomainEvents);
        });

        modelBuilder.Entity<EmergencyContact>(entity =>
        {
            entity.ToTable("EmergencyContact", Schema);
            entity.Property(contact => contact.FullName).HasMaxLength(200).IsRequired();
            entity.Property(contact => contact.RelationshipCode).HasMaxLength(64).IsRequired();
            entity.Property(contact => contact.Phone).HasMaxLength(32).IsRequired();
            entity.Property(contact => contact.Email).HasMaxLength(256);
            entity.HasIndex(contact => new { contact.TenantId, contact.EmployeeId }).HasDatabaseName("IX_EmergencyContact_TenantId_EmployeeId");
            entity.Ignore(contact => contact.DomainEvents);
        });
    }

    private static void ConfigureAddress(OwnedNavigationBuilder<EmployeeEntity, HomeAddress> address, string prefix)
    {
        address.Property(value => value.Line1).HasColumnName($"{prefix}AddressLine1").HasMaxLength(200);
        address.Property(value => value.Line2).HasColumnName($"{prefix}AddressLine2").HasMaxLength(200);
        address.Property(value => value.City).HasColumnName($"{prefix}City").HasMaxLength(100);
        address.Property(value => value.StateCode).HasColumnName($"{prefix}StateCode").HasMaxLength(64);
        address.Property(value => value.CountryCode).HasColumnName($"{prefix}CountryCode").HasMaxLength(8);
        address.Property(value => value.PostalCode).HasColumnName($"{prefix}PostalCode").HasMaxLength(16);
    }

    /// <summary>Two columns per protected value: the ciphertext, and the masked hint that is safe to show.</summary>
    private static void ConfigureProtected(
        EntityTypeBuilder<EmployeeEntity> entity,
        System.Linq.Expressions.Expression<Func<EmployeeEntity, ProtectedValue?>> navigation,
        string prefix)
        => entity.OwnsOne(navigation, value =>
        {
            value.Property(protectedValue => protectedValue.Ciphertext).HasColumnName($"{prefix}Ciphertext").HasMaxLength(1000);
            value.Property(protectedValue => protectedValue.Hint).HasColumnName($"{prefix}Hint").HasMaxLength(64);
        });
}

internal static class EmployeeData
{
    extension(AppDbContext db)
    {
        public DbSet<EmployeeEntity> Employees => db.Set<EmployeeEntity>();

        public DbSet<EmploymentRecord> EmploymentRecords => db.Set<EmploymentRecord>();

        public DbSet<EmployeeDependent> EmployeeDependents => db.Set<EmployeeDependent>();

        public DbSet<EmergencyContact> EmergencyContacts => db.Set<EmergencyContact>();
    }
}
