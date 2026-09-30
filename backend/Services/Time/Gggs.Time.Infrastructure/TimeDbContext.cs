using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Holidays;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Infrastructure;

/// <summary>Time's database: attendance (shifts/rosters/punches/days/regularizations), leave
/// types/policies/ledger/balances/requests, holiday calendars, and the employee snapshot fed from
/// Core HR.</summary>
public sealed class TimeDbContext(DbContextOptions<TimeDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "leave";

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();

    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();

    public DbSet<LeavePolicyRule> LeavePolicyRules => Set<LeavePolicyRule>();

    public DbSet<LeaveLedgerEntry> LeaveLedgerEntries => Set<LeaveLedgerEntry>();

    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();

    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<LeaveRequestDay> LeaveRequestDays => Set<LeaveRequestDay>();

    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<ShiftRoster> ShiftRosters => Set<ShiftRoster>();

    public DbSet<AttendancePunch> AttendancePunches => Set<AttendancePunch>();

    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();

    public DbSet<AttendanceRegularization> AttendanceRegularizations => Set<AttendanceRegularization>();

    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LeaveType>(entity =>
        {
            entity.ToTable("LeaveType");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Unit).HasConversion<byte>();
            entity.Property(x => x.GenderRestrictionCode).HasMaxLength(20);
            entity.Property(x => x.ColorHex).HasMaxLength(7).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_LeaveType_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeavePolicy>(entity =>
        {
            entity.ToTable("LeavePolicy");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeavePolicyRule>(entity =>
        {
            entity.ToTable("LeavePolicyRule");
            entity.Property(x => x.AccrualFrequency).HasConversion<byte>();
            entity.Property(x => x.AccrualAmount).HasColumnType("decimal(6,2)");
            entity.Property(x => x.MaxBalance).HasColumnType("decimal(6,2)");
            entity.Property(x => x.CarryForwardMax).HasColumnType("decimal(6,2)");
            entity.Property(x => x.MinDaysPerRequest).HasColumnType("decimal(6,2)");
            entity.Property(x => x.NegativeLimit).HasColumnType("decimal(6,2)");

            entity.HasIndex(x => new { x.TenantId, x.PolicyId, x.LeaveTypeId })
                .HasDatabaseName("UX_LeavePolicyRule_TenantId_PolicyId_LeaveTypeId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeaveLedgerEntry>(entity =>
        {
            entity.ToTable("LeaveLedger");
            entity.Property(x => x.TransactionType).HasConversion<byte>();
            entity.Property(x => x.Days).HasColumnType("decimal(6,2)");
            entity.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Remarks).HasMaxLength(500);

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.PeriodYear })
                .HasDatabaseName("IX_LeaveLedger_TenantId_EmployeeId_LeaveTypeId_PeriodYear");

            // The ledger service's own idempotency check - see LeaveLedgerService.PostAsync.
            entity.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.TransactionType })
                .HasDatabaseName("UX_LeaveLedger_TenantId_Source_TransactionType")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [SourceId] IS NOT NULL");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeaveBalance>(entity =>
        {
            entity.ToTable("LeaveBalance");
            entity.Property(x => x.Opening).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Accrued).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Taken).HasColumnType("decimal(6,2)");
            entity.Property(x => x.PendingApproval).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Adjusted).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Encashed).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Lapsed).HasColumnType("decimal(6,2)");
            entity.Property(x => x.CarriedForward).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Available).HasColumnType("decimal(6,2)");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.PeriodYear })
                .HasDatabaseName("UX_LeaveBalance_TenantId_EmployeeId_LeaveTypeId_PeriodYear")
                .IsUnique();

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.ToTable("LeaveRequest");
            entity.Property(x => x.RequestNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.FromSession).HasConversion<byte>();
            entity.Property(x => x.ToSession).HasConversion<byte>();
            entity.Property(x => x.TotalDays).HasColumnType("decimal(6,2)");
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.FromDate })
                .HasDatabaseName("IX_LeaveRequest_TenantId_EmployeeId_FromDate");

            entity.HasIndex(x => new { x.TenantId, x.Status, x.FromDate })
                .HasDatabaseName("IX_LeaveRequest_TenantId_Status_FromDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LeaveRequestDay>(entity =>
        {
            entity.ToTable("LeaveRequestDay");
            entity.Property(x => x.DayFraction).HasColumnType("decimal(3,2)");

            // A same-tenant composite FK, same convention as every child-of-another-aggregate
            // table (EmploymentRecord.EmployeeId in Core HR): no navigation property either side,
            // just an id column and the FK constraint itself - see LeaveRequest.Submit's own
            // remarks on why this is not a parent-owned collection.
            entity.HasIndex(x => new { x.TenantId, x.LeaveDate })
                .HasDatabaseName("IX_LeaveRequestDay_TenantId_LeaveDate");

            entity.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.LeaveDate })
                .HasDatabaseName("UX_LeaveRequestDay_TenantId_LeaveRequestId_LeaveDate")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<HolidayCalendar>(entity =>
        {
            entity.ToTable("HolidayCalendar");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.StateCode).HasMaxLength(10);

            entity.HasIndex(x => new { x.TenantId, x.Year })
                .HasDatabaseName("UX_HolidayCalendar_TenantId_Year")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.ToTable("Holiday");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.HolidayCalendarId, x.HolidayDate })
                .HasDatabaseName("UX_Holiday_TenantId_HolidayCalendarId_HolidayDate")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("Shift", "time");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_Shift_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<ShiftRoster>(entity =>
        {
            entity.ToTable("ShiftRoster", "time");
            entity.Property(x => x.Source).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RosterDate })
                .HasDatabaseName("UX_ShiftRoster_TenantId_EmployeeId_RosterDate")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.RosterDate, x.ShiftId })
                .HasDatabaseName("IX_ShiftRoster_TenantId_RosterDate_ShiftId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<AttendancePunch>(entity =>
        {
            entity.ToTable("AttendancePunch", "time");
            entity.Property(x => x.Direction).HasConversion<byte>();
            entity.Property(x => x.Source).HasConversion<byte>();
            entity.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
            entity.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
            entity.Property(x => x.IpAddress).HasMaxLength(45);
            entity.Property(x => x.ExternalRef).HasMaxLength(100);

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PunchTimeUtc })
                .HasDatabaseName("IX_AttendancePunch_TenantId_EmployeeId_PunchTimeUtc");

            // Device/import idempotency - see AttendancePunch.ExternalRef's own remarks.
            entity.HasIndex(x => new { x.TenantId, x.Source, x.ExternalRef })
                .HasDatabaseName("UX_AttendancePunch_TenantId_Source_ExternalRef")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [ExternalRef] IS NOT NULL");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<AttendanceDay>(entity =>
        {
            entity.ToTable("AttendanceDay", "time");
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AttendanceDate })
                .HasDatabaseName("UX_AttendanceDay_TenantId_EmployeeId_AttendanceDate")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.AttendanceDate, x.Status })
                .HasDatabaseName("IX_AttendanceDay_TenantId_AttendanceDate_Status");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<AttendanceRegularization>(entity =>
        {
            entity.ToTable("AttendanceRegularization", "time");
            entity.Property(x => x.ReasonCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Remarks).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AttendanceDate })
                .HasDatabaseName("IX_AttendanceRegularization_TenantId_EmployeeId_AttendanceDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OvertimeRequest>(entity =>
        {
            entity.ToTable("OvertimeRequest", "time");
            entity.Property(x => x.ReasonCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Remarks).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.OvertimeDate })
                .HasDatabaseName("IX_OvertimeRequest_TenantId_EmployeeId_OvertimeDate");

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
