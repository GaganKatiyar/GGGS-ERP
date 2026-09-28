using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Files;
using Gggs.Documents.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace Gggs.Documents.Application.Documents;

/// <summary>What the caller supplies for a new upload.</summary>
public sealed record UploadDocumentRequest(
    string DocumentTypeCode,
    DocumentOwnerType OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    Guid UploadedByUserId,
    DateOnly? ExpiryDate);

/// <summary>
/// Uploading a file: validates it against its document type's rules, stores the bytes, and records
/// the result - in that order, so a rejected upload never touches storage and a storage failure
/// never leaves a database row pointing at bytes that do not exist.
/// </summary>
public sealed class DocumentUploadService(
    IDocumentTypeRepository types,
    IDocumentRepository documents,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    ILogger<DocumentUploadService> logger)
{
    public async Task<Document> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normalizedCode = DocumentType.NormalizeCode(request.DocumentTypeCode);
        DocumentType documentType = await types.GetByCodeAsync(normalizedCode, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentType), normalizedCode);

        string extension = Path.GetExtension(request.FileName).TrimStart('.');
        Guard.Against(
            !documentType.AllowsExtension(extension),
            "DOCUMENT_EXTENSION_NOT_ALLOWED",
            $"'{extension}' files are not allowed for document type '{documentType.Code}'.");

        long maxSizeBytes = documentType.MaxSizeMb * 1024L * 1024L;
        Guard.Against(
            request.SizeBytes > maxSizeBytes,
            "DOCUMENT_TOO_LARGE",
            $"The file exceeds the {documentType.MaxSizeMb} MB limit for document type '{documentType.Code}'.");

        // A fresh Guid rather than the document's own id: the id is not assigned until Document.Upload
        // returns, and the key needs to exist before that call happens.
        Guid documentGroupId = Guid.CreateVersion7();
        string relativeKey = $"{request.OwnerType.ToString().ToLowerInvariant()}/{request.OwnerId:N}/{documentGroupId:N}/v1/{SanitizeFileName(request.FileName)}";

        StoredFile stored = await fileStorage.SaveAsync(
            new FileToStore
            {
                Content = request.Content,
                RelativeKey = relativeKey,
                ContentType = request.ContentType,
            },
            cancellationToken);

        Document document = Document.Upload(
            request.OwnerType,
            request.OwnerId,
            documentType.Id,
            request.FileName,
            request.ContentType,
            stored.SizeBytes,
            stored.StorageKey,
            stored.Sha256,
            request.UploadedByUserId,
            request.ExpiryDate);

        documents.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Document {DocumentId} ({FileName}, {SizeBytes} bytes) uploaded for {OwnerType} {OwnerId} as {DocumentTypeCode}.",
            document.Id, document.FileName, document.SizeBytes, document.OwnerType, document.OwnerId, documentType.Code);

        return document;
    }

    /// <summary>Keeps the caller's own name for display and download, but strips anything that would
    /// not survive as a path segment on every backing store.</summary>
    private static string SanitizeFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]);
    }
}
