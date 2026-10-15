using Gggs.Data;
using Gggs.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Attendance.Infrastructure;

/// <summary>The Attendance module's tables, in the <c>att</c> schema.</summary>
internal sealed class AttendanceModel : IModuleModel
{
    public const string Schema = "att";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AttendancePunch>(entity =>
        {
            entity.ToTable("AttendancePunch", Schema);
            entity.Property(punch => punch.Source).HasConversion<byte>();
            entity.Property(punch => punch.IpAddress).HasMaxLength(64);
            entity.Property(punch => punch.Note).HasMaxLength(200);
            entity.HasIndex(punch => new { punch.TenantId, punch.EmployeeId, punch.PunchedAt }).HasDatabaseName("IX_AttendancePunch_TenantId_Employee_At");
            entity.Ignore(punch => punch.DomainEvents);
        });

        modelBuilder.Entity<AttendanceDay>(entity =>
        {
            entity.ToTable("AttendanceDay", Schema);
            entity.Property(day => day.ShiftName).HasMaxLength(100).IsRequired();
            entity.Property(day => day.Status).HasConversion<byte>();
            entity.Property(day => day.Note).HasMaxLength(200);
            entity.HasIndex(day => new { day.TenantId, day.EmployeeId, day.Date })
                .HasDatabaseName("UX_AttendanceDay_TenantId_Employee_Date")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(day => new { day.TenantId, day.Date, day.Status }).HasDatabaseName("IX_AttendanceDay_TenantId_Date_Status");
            entity.Ignore(day => day.DomainEvents);
        });

        modelBuilder.Entity<RegularizationRequest>(entity =>
        {
            entity.ToTable("RegularizationRequest", Schema);
            entity.Property(request => request.Reason).HasMaxLength(300).IsRequired();
            entity.Property(request => request.Status).HasConversion<byte>();
            entity.HasIndex(request => new { request.TenantId, request.EmployeeId, request.Date }).HasDatabaseName("IX_RegularizationRequest_TenantId_Employee_Date");
            entity.HasIndex(request => new { request.TenantId, request.Status }).HasDatabaseName("IX_RegularizationRequest_TenantId_Status");
            entity.Ignore(request => request.DomainEvents);
        });

        modelBuilder.Entity<AttendanceDevice>(entity =>
        {
            entity.ToTable("AttendanceDevice", Schema);
            entity.Property(device => device.Code).HasMaxLength(32).IsRequired();
            entity.Property(device => device.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(device => new { device.TenantId, device.Code })
                .HasDatabaseName("UX_AttendanceDevice_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(device => device.DomainEvents);
        });
    }
}

internal static class AttendanceData
{
    extension(AppDbContext db)
    {
        public DbSet<AttendancePunch> AttendancePunches => db.Set<AttendancePunch>();

        public DbSet<AttendanceDay> AttendanceDays => db.Set<AttendanceDay>();

        public DbSet<RegularizationRequest> Regularizations => db.Set<RegularizationRequest>();

        public DbSet<AttendanceDevice> AttendanceDevices => db.Set<AttendanceDevice>();
    }
}
