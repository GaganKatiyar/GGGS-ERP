using Gggs.Data;
using Gggs.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Documents.Infrastructure;

/// <summary>The Documents module's tables, in the <c>doc</c> schema. File contents live in file storage, not here.</summary>
internal sealed class DocumentsModel : IModuleModel
{
    public const string Schema = "doc";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.ToTable("DocumentType", Schema);
            entity.Property(type => type.Code).HasMaxLength(32).IsRequired();
            entity.Property(type => type.Name).HasMaxLength(200).IsRequired();
            entity.Property(type => type.Category).HasConversion<byte>();
            entity.HasIndex(type => new { type.TenantId, type.Code })
                .HasDatabaseName("UX_DocumentType_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(type => type.DomainEvents);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Document", Schema);
            entity.Property(document => document.Title).HasMaxLength(200).IsRequired();
            entity.Property(document => document.OwnerType).HasConversion<byte>();
            entity.Property(document => document.Source).HasConversion<byte>();
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(document => document.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);

            // A person's documents, and the expiry report.
            entity.HasIndex(document => new { document.TenantId, document.OwnerType, document.OwnerId, document.IsActive })
                .HasDatabaseName("IX_Document_TenantId_Owner");
            entity.HasIndex(document => new { document.TenantId, document.ExpiresOn })
                .HasDatabaseName("IX_Document_TenantId_ExpiresOn")
                .HasFilter("[ExpiresOn] IS NOT NULL");
            entity.Ignore(document => document.DomainEvents);
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("DocumentVersion", Schema);
            entity.Property(version => version.FileName).HasMaxLength(255).IsRequired();
            entity.Property(version => version.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(version => version.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(version => version.StorageKey).HasMaxLength(1000).IsRequired();
            entity.Property(version => version.Note).HasMaxLength(500);
            entity.HasOne<Document>().WithMany().HasForeignKey(version => version.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(version => new { version.TenantId, version.DocumentId, version.Number })
                .HasDatabaseName("UX_DocumentVersion_TenantId_Document_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(version => version.DomainEvents);
        });

        modelBuilder.Entity<DocumentTemplate>(entity =>
        {
            entity.ToTable("DocumentTemplate", Schema);
            entity.Property(template => template.Code).HasMaxLength(32).IsRequired();
            entity.Property(template => template.Name).HasMaxLength(200).IsRequired();
            entity.Property(template => template.Description).HasMaxLength(500);
            entity.Property(template => template.Body).IsRequired();
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(template => template.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(template => new { template.TenantId, template.Code })
                .HasDatabaseName("UX_DocumentTemplate_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(template => template.DomainEvents);
        });

        modelBuilder.Entity<DocumentSignature>(entity =>
        {
            entity.ToTable("DocumentSignature", Schema);
            entity.Property(request => request.Status).HasConversion<byte>();
            entity.Property(request => request.Provider).HasMaxLength(32).IsRequired();
            entity.Property(request => request.ExternalReference).HasMaxLength(200);
            entity.Property(request => request.Comment).HasMaxLength(500);
            entity.Property(request => request.SignedFromIp).HasMaxLength(64);
            entity.HasOne<Document>().WithMany().HasForeignKey(request => request.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(request => new { request.TenantId, request.SignerEmployeeId, request.Status })
                .HasDatabaseName("IX_DocumentSignature_TenantId_Signer");
            entity.HasIndex(request => new { request.TenantId, request.DocumentId }).HasDatabaseName("IX_DocumentSignature_TenantId_DocumentId");
            entity.Ignore(request => request.DomainEvents);
        });
    }
}

internal static class DocumentsData
{
    extension(AppDbContext db)
    {
        public DbSet<DocumentType> DocumentTypes => db.Set<DocumentType>();

        public DbSet<Document> Documents => db.Set<Document>();

        public DbSet<DocumentVersion> DocumentVersions => db.Set<DocumentVersion>();

        public DbSet<DocumentTemplate> DocumentTemplates => db.Set<DocumentTemplate>();

        public DbSet<DocumentSignature> DocumentSignatures => db.Set<DocumentSignature>();
    }
}
