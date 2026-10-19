using Gggs.Data;
using Gggs.Modules.Assets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Assets.Infrastructure;

/// <summary>The Assets module's tables, in the <c>ast</c> schema.</summary>
internal sealed class AssetModel : IModuleModel
{
    public const string Schema = "ast";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AssetCategory>(entity =>
        {
            entity.ToTable("Category", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_Category_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("Asset", Schema);
            entity.Property(row => row.Tag).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Make).HasMaxLength(100);
            entity.Property(row => row.Model).HasMaxLength(100);
            entity.Property(row => row.SerialNumber).HasMaxLength(100);
            entity.Property(row => row.PurchaseCost).HasPrecision(18, 2);
            entity.Property(row => row.Vendor).HasMaxLength(200);
            entity.Property(row => row.Notes).HasMaxLength(2000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Condition).HasConversion<byte>();
            entity.HasOne<AssetCategory>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Tag }).HasDatabaseName("UX_Asset_TenantId_Tag").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.AssignedEmployeeId }).HasDatabaseName("IX_Asset_TenantId_AssignedEmployee");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Asset_TenantId_Status");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AssetEvent>(entity =>
        {
            entity.ToTable("AssetEvent", Schema);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.Cost).HasPrecision(18, 2);
            entity.Property(row => row.Note).HasMaxLength(1000);
            entity.HasOne<Asset>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.AssetId }).HasDatabaseName("IX_AssetEvent_TenantId_Asset");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AssetRequest>(entity =>
        {
            entity.ToTable("Request", Schema);
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.Property(row => row.Response).HasMaxLength(1000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.HasOne<AssetCategory>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Request_TenantId_Employee");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class AssetData
{
    extension(AppDbContext db)
    {
        public DbSet<AssetCategory> AssetCategories => db.Set<AssetCategory>();

        public DbSet<Asset> Assets => db.Set<Asset>();

        public DbSet<AssetEvent> AssetEvents => db.Set<AssetEvent>();

        public DbSet<AssetRequest> AssetRequests => db.Set<AssetRequest>();
    }
}
