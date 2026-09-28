using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Documents.Domain.Documents;

/// <summary>What kind of record a document is filed against. No FK is possible - the owner lives in
/// another service's database - so this is the same decoupled-reference shape every cross-service
/// pointer in this system uses.</summary>
public enum DocumentOwnerType
{
    Employee = 1,
    LegalEntity = 2,
    Department = 3,

    /// <summary>Tenant-wide documents with no single owning record - a company policy, for example.</summary>
    Tenant = 4,
}

/// <summary>
/// Malware-scan outcome. Only <see cref="Skipped"/> is ever actually set by this slice: no scanner
/// adapter exists yet (architecture.md §15 calls for Defender for Storage in the cloud and a
/// ClamAV/ICAP adapter on-premise; neither is built). The other values are declared now so storing
/// a real scan result later is a status update, not a migration.
/// </summary>
public enum DocumentScanStatus
{
    Skipped = 0,
    Pending = 1,
    Clean = 2,
    Infected = 3,
    Failed = 4,
}

/// <summary>
/// One uploaded file and what is known about it.
/// <para>
/// Versioning columns (<see cref="DocumentGroupId"/>, <see cref="Version"/>,
/// <see cref="IsCurrentVersion"/>) match architecture.md §15's shape, but nothing in this slice
/// re-uploads onto an existing group - every upload is its own group of one. Re-upload-as-new-version
/// is a later addition, not a schema change, when it is needed.
/// </para>
/// </summary>
public sealed class Document : TenantAggregateRoot
{
    private Document()
    {
        FileName = null!;
        ContentType = null!;
        StorageKey = null!;
        Sha256 = null!;
    }

    public Guid DocumentGroupId { get; private set; }

    public int Version { get; private set; }

    public bool IsCurrentVersion { get; private set; }

    public DocumentOwnerType OwnerType { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Where <c>IFileStorage</c> actually put the bytes - includes the tenant container prefix.</summary>
    public string StorageKey { get; private set; }

    public string Sha256 { get; private set; }

    public DocumentScanStatus ScanStatus { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public static Document Upload(
        DocumentOwnerType ownerType,
        Guid ownerId,
        Guid documentTypeId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        string sha256,
        Guid uploadedByUserId,
        DateOnly? expiryDate)
    {
        Guard.NotEmpty(ownerId);
        Guard.NotEmpty(documentTypeId);
        Guard.NotEmpty(uploadedByUserId);
        Guard.Against(sizeBytes <= 0, "DOCUMENT_EMPTY", "An uploaded file must not be empty.");

        var document = new Document
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            DocumentTypeId = documentTypeId,
            FileName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fileName).Trim(), 260),
            ContentType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(contentType).Trim(), 200),
            SizeBytes = sizeBytes,
            StorageKey = Guard.MaxLength(Guard.NotNullOrWhiteSpace(storageKey), 900),
            Sha256 = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sha256), 64),
            ScanStatus = DocumentScanStatus.Skipped,
            UploadedByUserId = uploadedByUserId,
            ExpiryDate = expiryDate,
            Version = 1,
            IsCurrentVersion = true,
        };

        // A brand-new upload is the only member of its own version group; Id is not assigned until
        // the object initializer above finishes, so it is set here rather than in the initializer.
        document.DocumentGroupId = document.Id;
        return document;
    }
}
