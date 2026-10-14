using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Documents.Domain;

public enum DocumentCategory
{
    Identity = 1,
    Education = 2,
    Employment = 3,
    Letter = 4,
    Policy = 5,
    Contract = 6,
    Other = 7,
}

/// <summary>A kind of document - "PAN card", "Offer letter" - with the rules every document of that kind follows.</summary>
public sealed class DocumentType : TenantAggregateRoot
{
    private DocumentType()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public DocumentCategory Category { get; private set; }

    /// <summary>Whether employees see their own documents of this kind in self-service.</summary>
    public bool EmployeeVisible { get; private set; }

    public bool RequiresExpiry { get; private set; }

    public bool IsSystem { get; private set; }

    public static DocumentType Create(string code, string name, DocumentCategory category, bool employeeVisible, bool requiresExpiry, bool isSystem = false) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        Category = Enum.IsDefined(category) ? category : throw new DomainRuleException("DOCUMENT_CATEGORY", "Unknown document category."),
        EmployeeVisible = employeeVisible,
        RequiresExpiry = requiresExpiry,
        IsSystem = isSystem,
    };

    public void Update(string name, DocumentCategory category, bool employeeVisible, bool requiresExpiry)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Category = Enum.IsDefined(category) ? category : throw new DomainRuleException("DOCUMENT_CATEGORY", "Unknown document category.");
        EmployeeVisible = employeeVisible;
        RequiresExpiry = requiresExpiry;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum DocumentOwnerType
{
    Employee = 1,
    Organization = 2,
}

public enum DocumentSource
{
    Uploaded = 1,
    Generated = 2,
}

/// <summary>
/// A document and its history: every upload after the first is a new version, and nothing is
/// overwritten. Archiving hides it from lists without losing it.
/// </summary>
public sealed class Document : TenantAggregateRoot
{
    private Document()
    {
        Title = null!;
    }

    public DocumentOwnerType OwnerType { get; private set; }

    /// <summary>The employee it belongs to; null for organization documents.</summary>
    public Guid? OwnerId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public string Title { get; private set; }

    public int CurrentVersion { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public bool EmployeeVisible { get; private set; }

    public DocumentSource Source { get; private set; }

    public static Document Create(
        DocumentOwnerType ownerType,
        Guid? ownerId,
        DocumentType type,
        string title,
        DateOnly? expiresOn,
        bool? employeeVisible,
        DocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (ownerType == DocumentOwnerType.Employee && ownerId is null)
        {
            throw new DomainRuleException("DOCUMENT_OWNER_REQUIRED", "An employee document needs the employee it belongs to.");
        }

        if (type.RequiresExpiry && expiresOn is null)
        {
            throw new DomainRuleException("DOCUMENT_EXPIRY_REQUIRED", $"{type.Name} documents need an expiry date.");
        }

        return new Document
        {
            OwnerType = ownerType,
            OwnerId = ownerType == DocumentOwnerType.Employee ? ownerId : null,
            DocumentTypeId = type.Id,
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            ExpiresOn = expiresOn,
            EmployeeVisible = ownerType == DocumentOwnerType.Employee && (employeeVisible ?? type.EmployeeVisible),
            Source = source,
        };
    }

    public int NextVersion() => ++CurrentVersion;

    public void Archive() => IsActive = false;

    public void Restore() => IsActive = true;
}

/// <summary>One stored file of a document. Immutable once written.</summary>
public sealed class DocumentVersion : TenantAggregateRoot
{
    private DocumentVersion()
    {
        FileName = null!;
        ContentType = null!;
        Sha256 = null!;
        StorageKey = null!;
    }

    public Guid DocumentId { get; private set; }

    public int Number { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public string? Note { get; private set; }

    public static DocumentVersion Create(Guid documentId, int number, string fileName, string contentType, long sizeBytes, string sha256, string storageKey, string? note) => new()
    {
        DocumentId = documentId,
        Number = number,
        FileName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fileName), 255),
        ContentType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(contentType), 100),
        SizeBytes = sizeBytes,
        Sha256 = Guard.MaxLength(sha256, 64),
        StorageKey = Guard.MaxLength(storageKey, 1000),
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 500),
    };
}

/// <summary>
/// A letter written once and filled in per employee: <c>{{employeeName}}</c>, <c>{{designation}}</c>,
/// <c>{{today}}</c>. Generating one produces an ordinary document with a first version.
/// </summary>
public sealed class DocumentTemplate : TenantAggregateRoot
{
    private DocumentTemplate()
    {
        Code = null!;
        Name = null!;
        Body = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    /// <summary>HTML, with placeholders. Values are HTML-encoded when filled in.</summary>
    public string Body { get; private set; }

    public static DocumentTemplate Create(string code, string name, string? description, Guid documentTypeId, string body) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500),
        DocumentTypeId = documentTypeId,
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body), 100_000),
    };

    public void Update(string name, string? description, Guid documentTypeId, string body)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
        DocumentTypeId = documentTypeId;
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body), 100_000);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum SignatureStatus
{
    Requested = 1,

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "'Signed' is the domain word for a completed signature, not a type name.")]
    Signed = 2,
    Declined = 3,
    Cancelled = 4,
}

/// <summary>
/// A request for one employee to sign one version of a document. Handled by a signature provider
/// (spec §107 e-sign port): the built-in one records a signed acknowledgement in PeopleOS; an
/// external e-sign service plugs in behind the same port.
/// </summary>
public sealed class DocumentSignature : TenantAggregateRoot
{
    private DocumentSignature()
    {
        Provider = null!;
    }

    public Guid DocumentId { get; private set; }

    public int VersionNumber { get; private set; }

    public Guid SignerEmployeeId { get; private set; }

    public SignatureStatus Status { get; private set; }

    public string Provider { get; private set; }

    public string? ExternalReference { get; private set; }

    public DateTime RequestedDate { get; private set; }

    public DateTime? CompletedDate { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Where the signature was given from, as evidence alongside the timestamp.</summary>
    public string? SignedFromIp { get; private set; }

    public static DocumentSignature Create(Guid documentId, int versionNumber, Guid signerEmployeeId, string provider, DateTime now) => new()
    {
        DocumentId = documentId,
        VersionNumber = versionNumber,
        SignerEmployeeId = Guard.NotEmpty(signerEmployeeId),
        Status = SignatureStatus.Requested,
        Provider = Guard.MaxLength(provider, 32),
        RequestedDate = now,
    };

    public void AttachReference(string? externalReference)
        => ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : Guard.MaxLength(externalReference, 200);

    public void Sign(DateTime now, string? ipAddress)
    {
        EnsureRequested();
        Status = SignatureStatus.Signed;
        CompletedDate = now;
        SignedFromIp = ipAddress is null ? null : Guard.MaxLength(ipAddress, 64);
    }

    public void Decline(DateTime now, string? comment)
    {
        EnsureRequested();
        Status = SignatureStatus.Declined;
        CompletedDate = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 500);
    }

    public void Cancel(DateTime now)
    {
        EnsureRequested();
        Status = SignatureStatus.Cancelled;
        CompletedDate = now;
    }

    private void EnsureRequested()
    {
        if (Status != SignatureStatus.Requested)
        {
            throw new DomainRuleException("SIGNATURE_CLOSED", $"This signature request is already {Status}.");
        }
    }
}
