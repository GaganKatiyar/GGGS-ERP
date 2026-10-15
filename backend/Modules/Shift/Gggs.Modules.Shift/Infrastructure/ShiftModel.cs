using Gggs.Data;
using Gggs.Modules.Shift.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Shift.Infrastructure;

/// <summary>The Shift module's tables, in the <c>shf</c> schema.</summary>
internal sealed class ShiftModel : IModuleModel
{
    public const string Schema = "shf";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WorkShift>(entity =>
        {
            entity.ToTable("Shift", Schema);
            entity.Property(shift => shift.Code).HasMaxLength(32).IsRequired();
            entity.Property(shift => shift.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(shift => new { shift.TenantId, shift.Code })
                .HasDatabaseName("UX_Shift_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(shift => new { shift.TenantId, shift.IsDefault })
                .HasDatabaseName("UX_Shift_TenantId_Default")
                .IsUnique()
                .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");
            entity.Ignore(shift => shift.DurationMinutes);
            entity.Ignore(shift => shift.WeeklyOffDays);
            entity.Ignore(shift => shift.DomainEvents);
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.ToTable("ShiftAssignment", Schema);
            entity.HasOne<WorkShift>().WithMany().HasForeignKey(assignment => assignment.ShiftId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => new { assignment.TenantId, assignment.EmployeeId, assignment.EffectiveFrom })
                .HasDatabaseName("IX_ShiftAssignment_TenantId_Employee");
            entity.Ignore(assignment => assignment.DomainEvents);
        });

        modelBuilder.Entity<RosterOverride>(entity =>
        {
            entity.ToTable("RosterOverride", Schema);
            entity.Property(entry => entry.Reason).HasMaxLength(300);
            entity.HasOne<WorkShift>().WithMany().HasForeignKey(entry => entry.ShiftId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(entry => new { entry.TenantId, entry.EmployeeId, entry.Date })
                .HasDatabaseName("UX_RosterOverride_TenantId_Employee_Date")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(entry => entry.DomainEvents);
        });

        modelBuilder.Entity<ShiftSwapRequest>(entity =>
        {
            entity.ToTable("ShiftSwapRequest", Schema);
            entity.Property(swap => swap.Reason).HasMaxLength(300);
            entity.Property(swap => swap.Status).HasConversion<byte>();
            entity.HasIndex(swap => new { swap.TenantId, swap.RequesterEmployeeId, swap.Date }).HasDatabaseName("IX_ShiftSwapRequest_TenantId_Requester");
            entity.Ignore(swap => swap.DomainEvents);
        });
    }
}

internal static class ShiftData
{
    extension(AppDbContext db)
    {
        public DbSet<WorkShift> Shifts => db.Set<WorkShift>();

        public DbSet<ShiftAssignment> ShiftAssignments => db.Set<ShiftAssignment>();

        public DbSet<RosterOverride> RosterOverrides => db.Set<RosterOverride>();

        public DbSet<ShiftSwapRequest> ShiftSwapRequests => db.Set<ShiftSwapRequest>();
    }
}
