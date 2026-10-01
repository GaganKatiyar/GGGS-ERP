using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Spend.Infrastructure;

/// <summary>Spend's database: expense categories, claims and their lines, travel requests with
/// their advances and settlements, and the employee snapshot fed from Core HR.</summary>
public sealed class SpendDbContext(DbContextOptions<SpendDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "exp";

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();

    public DbSet<ExpenseLine> ExpenseLines => Set<ExpenseLine>();

    public DbSet<TravelRequest> TravelRequests => Set<TravelRequest>();

    public DbSet<TravelAdvance> TravelAdvances => Set<TravelAdvance>();

    public DbSet<TravelSettlement> TravelSettlements => Set<TravelSettlement>();

    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ExpenseCategory>(entity =>
        {
            entity.ToTable("ExpenseCategory", "exp");
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_ExpenseCategory_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<ExpenseClaim>(entity =>
        {
            entity.ToTable("ExpenseClaim", "exp");
            entity.Property(x => x.ClaimNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Total).HasColumnType("decimal(14,2)");
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.ClaimNo })
                .HasDatabaseName("UX_ExpenseClaim_TenantId_ClaimNo")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("IX_ExpenseClaim_TenantId_EmployeeId");

            entity.HasIndex(x => new { x.TenantId, x.TravelRequestId })
                .HasDatabaseName("IX_ExpenseClaim_TenantId_TravelRequestId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<ExpenseLine>(entity =>
        {
            entity.ToTable("ExpenseLine", "exp");
            entity.Property(x => x.Amount).HasColumnType("decimal(14,2)");
            entity.Property(x => x.Merchant).HasMaxLength(200);

            entity.HasIndex(x => new { x.TenantId, x.ClaimId })
                .HasDatabaseName("IX_ExpenseLine_TenantId_ClaimId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<TravelRequest>(entity =>
        {
            entity.ToTable("TravelRequest", "exp");
            entity.Property(x => x.RequestNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FromCity).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ToCity).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EstimatedCost).HasColumnType("decimal(14,2)");
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.RequestNo })
                .HasDatabaseName("UX_TravelRequest_TenantId_RequestNo")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("IX_TravelRequest_TenantId_EmployeeId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<TravelAdvance>(entity =>
        {
            entity.ToTable("TravelAdvance", "exp");
            entity.Property(x => x.Amount).HasColumnType("decimal(14,2)");
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.TravelRequestId })
                .HasDatabaseName("IX_TravelAdvance_TenantId_TravelRequestId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<TravelSettlement>(entity =>
        {
            entity.ToTable("TravelSettlement", "exp");
            entity.Property(x => x.AdvanceTotal).HasColumnType("decimal(14,2)");
            entity.Property(x => x.ExpenseTotal).HasColumnType("decimal(14,2)");
            entity.Property(x => x.NetPayable).HasColumnType("decimal(14,2)");
            entity.Property(x => x.NetRecoverable).HasColumnType("decimal(14,2)");
            entity.Property(x => x.Status).HasConversion<byte>();

            entity.HasIndex(x => new { x.TenantId, x.TravelRequestId })
                .HasDatabaseName("UX_TravelSettlement_TenantId_TravelRequestId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

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
