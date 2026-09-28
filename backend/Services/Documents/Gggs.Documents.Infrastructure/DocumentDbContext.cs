using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Documents.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Documents.Infrastructure;

/// <summary>The document database: document types and uploaded files, all tenant-owned.</summary>
public sealed class DocumentDbContext(DbContextOptions<DocumentDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "doc";

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.ToTable("DocumentType");
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasConversion<byte>();
            entity.Property(x => x.AllowedExtensionsCsv).HasMaxLength(500).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_DocumentType_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Document");
            entity.Property(x => x.OwnerType).HasConversion<byte>();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(900).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ScanStatus).HasConversion<byte>();

            // A same-tenant composite FK: the database itself refuses a document naming another
            // tenant's document type (§9, layer 4 of the isolation model).
            entity.HasOne<DocumentType>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.DocumentTypeId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            // The common read: "every document filed against this record" - list screens for an
            // employee profile, a legal entity, etc.
            entity.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId })
                .HasDatabaseName("IX_Document_TenantId_OwnerType_OwnerId")
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(x => new { x.TenantId, x.DocumentGroupId })
                .HasDatabaseName("IX_Document_TenantId_DocumentGroupId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
