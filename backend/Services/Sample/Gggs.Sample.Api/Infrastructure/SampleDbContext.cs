using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Sample.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Sample.Api.Infrastructure;

/// <summary>
/// The sample service's database. Note how little it has to say: the tenant filter, soft-delete
/// filter, clustered key layout, audit columns and rowversion all arrive from the shared
/// conventions, so a new table is isolated and auditable by default.
/// </summary>
public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    public const string Schema = "sample";

    protected override string DefaultSchema => Schema;

    public DbSet<SampleRecord> SampleRecords => Set<SampleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SampleRecord>(entity =>
        {
            entity.ToTable("SampleRecord");

            entity.Property(record => record.Code).HasMaxLength(30).IsRequired();
            entity.Property(record => record.Title).HasMaxLength(200).IsRequired();
            entity.Property(record => record.Notes).HasMaxLength(2000);

            // Unique per tenant and filtered on IsDeleted, so a code can be reused after deletion.
            entity.HasIndex(record => new { record.TenantId, record.Code })
                .HasDatabaseName("UX_SampleRecord_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        // The outbox lives in this service's own database, because the guarantee depends on it
        // sharing a transaction with the business change.
        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
