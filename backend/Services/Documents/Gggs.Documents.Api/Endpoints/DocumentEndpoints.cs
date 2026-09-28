using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Documents.Api.Contracts;
using Gggs.Documents.Application.Documents;
using Gggs.Documents.Domain.Documents;
using Gggs.Documents.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Documents.Api.Endpoints;

/// <summary>
/// Uploading, listing, downloading and deleting files.
/// <para>
/// No endpoint here ever returns a raw storage key or a byte stream except
/// <see cref="DownloadAsync"/>, and that one only after its own tenant, permission and existence
/// check - exactly the "sensitive sub-resources get their own endpoint" shape every other service
/// in this system uses for bank details and identity documents.
/// </para>
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/documents").WithTags("Documents");

        group.MapGet("/", ListByOwnerAsync).WithName("ListDocuments").RequirePermission(Permissions.Document.View);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetDocument").RequirePermission(Permissions.Document.View);
        group.MapGet("/{id:guid}/download", DownloadAsync)
            .WithName("DownloadDocument")
            .RequirePermission(Permissions.Document.View);

        group.MapPost("/", UploadAsync)
            .WithName("UploadDocument")
            .RequirePermission(Permissions.Document.Upload)
            .DisableAntiforgery();

        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteDocument").RequirePermission(Permissions.Document.Delete);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<DocumentResponse>>> ListByOwnerAsync(
        DocumentOwnerType ownerType,
        Guid ownerId,
        DocumentDbContext database,
        CancellationToken cancellationToken)
    {
        List<Domain.Documents.Document> documents = await database.Documents.AsNoTracking()
            .Where(x => x.OwnerType == ownerType && x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<DocumentResponse>>([.. documents.Select(ToResponse)]);
    }

    private static async Task<Results<Ok<DocumentResponse>, NotFound>> GetAsync(
        Guid id,
        DocumentDbContext database,
        CancellationToken cancellationToken)
    {
        Domain.Documents.Document? document = await database.Documents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return document is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(document));
    }

    /// <summary>
    /// Redirects to a signed link when the backing store can issue one (Azure Blob), or streams the
    /// file itself when it cannot (the on-premise file share) - the same fallback
    /// architecture.md §15 describes for high-sensitivity documents, applied here to every document
    /// because this slice has no per-type sensitivity routing yet (see <see cref="DocumentType"/>'s
    /// own remarks).
    /// </summary>
    private static async Task<Results<RedirectHttpResult, FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid id,
        DocumentDbContext database,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        Domain.Documents.Document? document = await database.Documents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (document is null)
        {
            return TypedResults.NotFound();
        }

        Uri? link = await fileStorage.TryCreateDownloadLinkAsync(
            document.StorageKey, TimeSpan.FromMinutes(5), document.FileName, cancellationToken);

        if (link is not null)
        {
            return TypedResults.Redirect(link.ToString());
        }

        Stream content = await fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);
        return TypedResults.File(content, document.ContentType, document.FileName);
    }

    private static async Task<Created<DocumentResponse>> UploadAsync(
        IFormFile file,
        [FromForm] string documentTypeCode,
        [FromForm] DocumentOwnerType ownerType,
        [FromForm] Guid ownerId,
        [FromForm] DateOnly? expiryDate,
        DocumentUploadService uploadService,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using Stream content = file.OpenReadStream();

        Domain.Documents.Document document = await uploadService.UploadAsync(
            new UploadDocumentRequest(
                documentTypeCode,
                ownerType,
                ownerId,
                file.FileName,
                file.ContentType,
                file.Length,
                content,
                currentUser.UserId,
                expiryDate),
            cancellationToken);

        DocumentResponse response = ToResponse(document);
        return TypedResults.Created($"/api/v1/documents/{document.Id}", response);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        IDocumentRepository documents,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        Domain.Documents.Document? document = await documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        // The bytes are left in place: TenantAuditSaveChangesInterceptor converts this to a
        // soft-delete, and a file a compliance or legal-hold rule still needs must stay retrievable
        // by an operator even after the record is hidden from ordinary reads.
        documents.Remove(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static DocumentResponse ToResponse(Domain.Documents.Document document) => new(
        document.Id,
        document.OwnerType,
        document.OwnerId,
        document.DocumentTypeId,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.Sha256,
        document.ScanStatus,
        document.UploadedByUserId,
        document.ExpiryDate,
        document.CreatedDate);
}
