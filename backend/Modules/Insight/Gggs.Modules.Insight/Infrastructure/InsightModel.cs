using Gggs.Data;
using Gggs.Modules.Insight.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Insight.Infrastructure;

/// <summary>The Insight module's tables, in the <c>ins</c> schema.</summary>
internal sealed class InsightModel : IModuleModel
{
    public const string Schema = "ins";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ReportSubscription>(entity =>
        {
            entity.ToTable("ReportSubscription", Schema);
            entity.Property(row => row.Area).HasMaxLength(30);
            entity.Property(row => row.Frequency).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.UserId, row.Area }).HasDatabaseName("UX_ReportSubscription_TenantId_User_Area").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ImportRun>(entity =>
        {
            entity.ToTable("ImportRun", Schema);
            entity.Property(row => row.HandlerKey).HasMaxLength(60);
            entity.Property(row => row.FileName).HasMaxLength(200);
            entity.HasIndex(row => new { row.TenantId, row.CreatedDate }).HasDatabaseName("IX_ImportRun_TenantId_Created");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<IntegrationConnection>(entity =>
        {
            entity.ToTable("IntegrationConnection", Schema);
            entity.Property(row => row.AdapterKey).HasMaxLength(60);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.SettingsJson).IsRequired();
            entity.Property(row => row.LastMessage).HasMaxLength(500);
            entity.HasIndex(row => new { row.TenantId, row.Name }).HasDatabaseName("UX_IntegrationConnection_TenantId_Name").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<IntegrationRun>(entity =>
        {
            entity.ToTable("IntegrationRun", Schema);
            entity.Property(row => row.Operation).HasMaxLength(60);
            entity.Property(row => row.Message).HasMaxLength(500);
            entity.Property(row => row.FileName).HasMaxLength(200);
            entity.HasOne<IntegrationConnection>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.ConnectionId, row.CreatedDate }).HasDatabaseName("IX_IntegrationRun_TenantId_Connection_Created");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class InsightData
{
    extension(AppDbContext db)
    {
        public DbSet<ReportSubscription> ReportSubscriptions => db.Set<ReportSubscription>();

        public DbSet<ImportRun> ImportRuns => db.Set<ImportRun>();

        public DbSet<IntegrationConnection> IntegrationConnections => db.Set<IntegrationConnection>();

        public DbSet<IntegrationRun> IntegrationRuns => db.Set<IntegrationRun>();
    }
}
