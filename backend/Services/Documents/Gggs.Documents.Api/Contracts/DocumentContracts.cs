using Gggs.Documents.Domain.Documents;

namespace Gggs.Documents.Api.Contracts;

public sealed record DocumentTypeResponse(
    Guid Id,
    string Code,
    string Name,
    DocumentCategory Category,
    IReadOnlyList<string> AllowedExtensions,
    int MaxSizeMb,
    bool IsEmployeeUploadable,
    bool IsSensitive);

public sealed record CreateDocumentTypeBody(
    string Code,
    string Name,
    DocumentCategory Category,
    IReadOnlyCollection<string> AllowedExtensions,
    int MaxSizeMb,
    bool IsEmployeeUploadable,
    bool IsSensitive);

public sealed record UpdateDocumentTypeRulesBody(
    IReadOnlyCollection<string> AllowedExtensions,
    int MaxSizeMb,
    bool IsEmployeeUploadable,
    bool IsSensitive);

/// <summary>
/// A document's metadata - never a URL or a byte stream, both of which are handed out only through
/// the dedicated download endpoint after its own permission and ownership check.
/// </summary>
public sealed record DocumentResponse(
    Guid Id,
    DocumentOwnerType OwnerType,
    Guid OwnerId,
    Guid DocumentTypeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DocumentScanStatus ScanStatus,
    Guid UploadedByUserId,
    DateOnly? ExpiryDate,
    DateTime CreatedDate);
