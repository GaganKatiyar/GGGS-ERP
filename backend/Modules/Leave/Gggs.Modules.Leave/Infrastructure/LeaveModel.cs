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

        // The per-type rules before named policies: read once, by LeavePlanUpgrade, and dropped later.
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

        modelBuilder.Entity<LeavePlan>(entity =>
        {
            entity.ToTable("LeavePlan", Schema);
            entity.Property(plan => plan.Code).HasMaxLength(32).IsRequired();
            entity.Property(plan => plan.Name).HasMaxLength(100).IsRequired();
            entity.Property(plan => plan.Description).HasMaxLength(500);
            entity.HasMany(plan => plan.Items).WithOne().HasForeignKey(item => item.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(plan => plan.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(plan => new { plan.TenantId, plan.Code })
                .HasDatabaseName("UX_LeavePlan_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(plan => plan.DomainEvents);
        });

        modelBuilder.Entity<LeavePlanItem>(entity =>
        {
            entity.ToTable("LeavePlanItem", Schema);
            entity.Property(item => item.AnnualEntitlement).HasPrecision(6, 2);
            entity.Property(item => item.CarryForwardLimit).HasPrecision(6, 2);
            entity.Property(item => item.MaxNegativeBalance).HasPrecision(6, 2);
            entity.Property(item => item.MinDaysPerRequest).HasPrecision(6, 2);
            entity.Property(item => item.DocumentRequiredAboveDays).HasPrecision(6, 2);
            entity.Property(item => item.MaxEncashDays).HasPrecision(6, 2);
            entity.Property(item => item.GrantPerOccurrence).HasPrecision(6, 2);
            entity.Property(item => item.CreditTiming).HasConversion<byte>().HasDefaultValue(CreditTiming.StartOfPeriod);
            entity.Property(item => item.JoiningCutoffDay).HasDefaultValue(15);
            entity.Property(item => item.ProrateOnJoining).HasDefaultValue(true);
            entity.Property(item => item.Accrual).HasConversion<byte>();
            entity.Property(item => item.EligibleGenderCodes).HasMaxLength(200).IsRequired();
            entity.HasOne<LeaveType>().WithMany().HasForeignKey(item => item.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.PlanId, item.LeaveTypeId })
                .HasDatabaseName("UX_LeavePlanItem_TenantId_Plan_Type")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(item => item.Genders);
            entity.Ignore(item => item.Rules);
            entity.Ignore(item => item.DomainEvents);
        });

        modelBuilder.Entity<LeavePlanAssignment>(entity =>
        {
            entity.ToTable("LeavePlanAssignment", Schema);
            entity.Property(assignment => assignment.EmploymentTypeCode).HasMaxLength(64).IsRequired();
            entity.HasOne<LeavePlan>().WithMany().HasForeignKey(assignment => assignment.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => new { assignment.TenantId, assignment.EmploymentTypeCode })
                .HasDatabaseName("UX_LeavePlanAssignment_TenantId_EmploymentType")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(assignment => assignment.DomainEvents);
        });

        modelBuilder.Entity<EmployeeLeavePlan>(entity =>
        {
            entity.ToTable("EmployeeLeavePlan", Schema);
            entity.Property(exception => exception.Reason).HasMaxLength(300);
            entity.HasOne<LeavePlan>().WithMany().HasForeignKey(exception => exception.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(exception => new { exception.TenantId, exception.EmployeeId, exception.EffectiveFrom })
                .HasDatabaseName("IX_EmployeeLeavePlan_TenantId_Employee_From");
            entity.Ignore(exception => exception.DomainEvents);
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

        modelBuilder.Entity<LeaveRequestAttachment>(entity =>
        {
            entity.ToTable("LeaveRequestAttachment", Schema);
            entity.Property(attachment => attachment.FileName).HasMaxLength(200).IsRequired();
            entity.Property(attachment => attachment.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(attachment => attachment.StorageKey).HasMaxLength(400).IsRequired();
            entity.HasOne<LeaveRequest>().WithMany().HasForeignKey(attachment => attachment.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(attachment => new { attachment.TenantId, attachment.RequestId })
                .HasDatabaseName("IX_LeaveRequestAttachment_TenantId_Request");
            entity.Ignore(attachment => attachment.DomainEvents);
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

        public DbSet<LeavePlan> LeavePlans => db.Set<LeavePlan>();

        public DbSet<LeavePlanItem> LeavePlanItems => db.Set<LeavePlanItem>();

        public DbSet<LeavePlanAssignment> LeavePlanAssignments => db.Set<LeavePlanAssignment>();

        public DbSet<EmployeeLeavePlan> EmployeeLeavePlans => db.Set<EmployeeLeavePlan>();

        public DbSet<LeaveLedgerEntry> LeaveLedger => db.Set<LeaveLedgerEntry>();

        public DbSet<LeaveRequest> LeaveRequests => db.Set<LeaveRequest>();

        public DbSet<LeaveRequestAttachment> LeaveRequestAttachments => db.Set<LeaveRequestAttachment>();

        public DbSet<HolidayCalendar> HolidayCalendars => db.Set<HolidayCalendar>();

        public DbSet<Holiday> Holidays => db.Set<Holiday>();
    }
}
