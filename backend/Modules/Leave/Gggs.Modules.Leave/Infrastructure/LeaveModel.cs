using Gggs.Data;
using Gggs.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Infrastructure;

/// <summary>The Leave module's tables, in the <c>lv</c> schema.</summary>
internal sealed class LeaveModel : IModuleModel
{
    public const string Schema = "lv";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LeaveType>(entity =>
        {
            entity.ToTable("LeaveType", Schema);
            entity.Property(type => type.Code).HasMaxLength(16).IsRequired();
            entity.Property(type => type.Name).HasMaxLength(100).IsRequired();
            entity.Property(type => type.Color).HasMaxLength(9).IsRequired();
            entity.HasIndex(type => new { type.TenantId, type.Code })
                .HasDatabaseName("UX_LeaveType_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(type => type.DomainEvents);
        });

        modelBuilder.Entity<LeavePolicy>(entity =>
        {
            entity.ToTable("LeavePolicy", Schema);
            entity.Property(policy => policy.EmploymentTypeCode).HasMaxLength(64);
            entity.Property(policy => policy.AnnualEntitlement).HasPrecision(6, 2);
            entity.Property(policy => policy.CarryForwardLimit).HasPrecision(6, 2);
            entity.Property(policy => policy.MaxNegativeBalance).HasPrecision(6, 2);
            entity.Property(policy => policy.Accrual).HasConversion<byte>();
            entity.HasOne<LeaveType>().WithMany().HasForeignKey(policy => policy.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(policy => new { policy.TenantId, policy.LeaveTypeId, policy.EmploymentTypeCode })
                .HasDatabaseName("IX_LeavePolicy_TenantId_Type");
            entity.Ignore(policy => policy.DomainEvents);
        });

        modelBuilder.Entity<LeaveLedgerEntry>(entity =>
        {
            entity.ToTable("LeaveLedgerEntry", Schema);
            entity.Property(entry => entry.Days).HasPrecision(6, 2);
            entity.Property(entry => entry.Kind).HasConversion<byte>();
            entity.Property(entry => entry.PeriodKey).HasMaxLength(32);
            entity.Property(entry => entry.Note).HasMaxLength(300);
            entity.HasOne<LeaveType>().WithMany().HasForeignKey(entry => entry.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);

            // A balance is the sum of these, read per person and type.
            entity.HasIndex(entry => new { entry.TenantId, entry.EmployeeId, entry.LeaveTypeId, entry.EntryDate })
                .HasDatabaseName("IX_LeaveLedgerEntry_TenantId_Employee_Type");

            // Scheduled entries happen once: re-running accrual for a month adds nothing.
            entity.HasIndex(entry => new { entry.TenantId, entry.EmployeeId, entry.LeaveTypeId, entry.PeriodKey })
                .HasDatabaseName("UX_LeaveLedgerEntry_TenantId_Employee_Type_Period")
                .IsUnique()
                .HasFilter("[PeriodKey] IS NOT NULL AND [IsDeleted] = 0");
            entity.Ignore(entry => entry.DomainEvents);
        });

        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.ToTable("LeaveRequest", Schema);
            entity.Property(request => request.Days).HasPrecision(6, 2);
            entity.Property(request => request.Reason).HasMaxLength(500);
            entity.Property(request => request.CancellationReason).HasMaxLength(300);
            entity.Property(request => request.Status).HasConversion<byte>();
            entity.HasOne<LeaveType>().WithMany().HasForeignKey(request => request.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(request => new { request.TenantId, request.EmployeeId, request.StartDate })
                .HasDatabaseName("IX_LeaveRequest_TenantId_Employee_Start");
            entity.HasIndex(request => new { request.TenantId, request.Status, request.StartDate })
                .HasDatabaseName("IX_LeaveRequest_TenantId_Status_Start");
            entity.Ignore(request => request.DomainEvents);
        });

        modelBuilder.Entity<HolidayCalendar>(entity =>
        {
            entity.ToTable("HolidayCalendar", Schema);
            entity.Property(calendar => calendar.Code).HasMaxLength(32).IsRequired();
            entity.Property(calendar => calendar.Name).HasMaxLength(100).IsRequired();
            entity.Property(calendar => calendar.LocationIdsJson).IsRequired();
            entity.HasIndex(calendar => new { calendar.TenantId, calendar.Code })
                .HasDatabaseName("UX_HolidayCalendar_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(calendar => calendar.LocationIds);
            entity.Ignore(calendar => calendar.DomainEvents);
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.ToTable("Holiday", Schema);
            entity.Property(holiday => holiday.Name).HasMaxLength(100).IsRequired();
            entity.HasOne<HolidayCalendar>().WithMany().HasForeignKey(holiday => holiday.CalendarId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(holiday => new { holiday.TenantId, holiday.CalendarId, holiday.Date })
                .HasDatabaseName("UX_Holiday_TenantId_Calendar_Date")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(holiday => holiday.DomainEvents);
        });
    }
}

internal static class LeaveData
{
    extension(AppDbContext db)
    {
        public DbSet<LeaveType> LeaveTypes => db.Set<LeaveType>();

        public DbSet<LeavePolicy> LeavePolicies => db.Set<LeavePolicy>();

        public DbSet<LeaveLedgerEntry> LeaveLedger => db.Set<LeaveLedgerEntry>();

        public DbSet<LeaveRequest> LeaveRequests => db.Set<LeaveRequest>();

        public DbSet<HolidayCalendar> HolidayCalendars => db.Set<HolidayCalendar>();

        public DbSet<Holiday> Holidays => db.Set<Holiday>();
    }
}
