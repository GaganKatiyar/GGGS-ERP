using Gggs.Data;
using Gggs.Modules.Timesheets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Timesheets.Infrastructure;

/// <summary>The Timesheets module's tables, in the <c>ts</c> schema.</summary>
internal sealed class TimesheetsModel : IModuleModel
{
    public const string Schema = "ts";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Timesheet>(entity =>
        {
            entity.ToTable("Timesheet", Schema);
            entity.Property(sheet => sheet.Status).HasConversion<byte>();
            entity.Property(sheet => sheet.TotalHours).HasPrecision(6, 2);
            entity.Property(sheet => sheet.BillableHours).HasPrecision(6, 2);
            entity.Property(sheet => sheet.Comment).HasMaxLength(1000);
            entity.Property(sheet => sheet.LastCorrectionReason).HasMaxLength(500);
            entity.HasIndex(sheet => new { sheet.TenantId, sheet.EmployeeId, sheet.WeekStart })
                .HasDatabaseName("UX_Timesheet_TenantId_Employee_Week")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(sheet => new { sheet.TenantId, sheet.WeekStart, sheet.Status }).HasDatabaseName("IX_Timesheet_TenantId_Week_Status");
            entity.Ignore(sheet => sheet.WeekEnd);
            entity.Ignore(sheet => sheet.DomainEvents);
        });

        modelBuilder.Entity<TimesheetEntry>(entity =>
        {
            entity.ToTable("TimesheetEntry", Schema);
            entity.Property(entry => entry.Hours).HasPrecision(5, 2);
            entity.Property(entry => entry.Category).HasConversion<byte>();
            entity.Property(entry => entry.Activity).HasMaxLength(50);
            entity.Property(entry => entry.Description).HasMaxLength(500);
            entity.HasOne<Timesheet>().WithMany().HasForeignKey(entry => entry.TimesheetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(entry => new { entry.TenantId, entry.TimesheetId }).HasDatabaseName("IX_TimesheetEntry_TenantId_Timesheet");
            entity.HasIndex(entry => new { entry.TenantId, entry.EmployeeId, entry.Date }).HasDatabaseName("IX_TimesheetEntry_TenantId_Employee_Date");
            entity.HasIndex(entry => new { entry.TenantId, entry.ProjectId, entry.Date }).HasDatabaseName("IX_TimesheetEntry_TenantId_Project_Date");
            entity.Ignore(entry => entry.DomainEvents);
        });

        modelBuilder.Entity<TimesheetSettings>(entity =>
        {
            entity.ToTable("TimesheetSettings", Schema);
            entity.Property(settings => settings.StandardHoursPerDay).HasPrecision(4, 2);
            entity.Property(settings => settings.MinHoursPerDay).HasPrecision(4, 2);
            entity.Property(settings => settings.MaxHoursPerDay).HasPrecision(4, 2);
            entity.Property(settings => settings.MinWeeklyHours).HasPrecision(5, 2);
            entity.HasIndex(settings => settings.TenantId)
                .HasDatabaseName("UX_TimesheetSettings_TenantId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(settings => settings.DomainEvents);
        });
    }
}

internal static class TimesheetsData
{
    extension(AppDbContext db)
    {
        public DbSet<Timesheet> Timesheets => db.Set<Timesheet>();

        public DbSet<TimesheetEntry> TimesheetEntries => db.Set<TimesheetEntry>();

        public DbSet<TimesheetSettings> TimesheetSettings => db.Set<TimesheetSettings>();
    }
}
