using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Documents.Domain;
using Gggs.Modules.Documents.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Documents.Application;

public sealed record DocumentItem(
    Guid Id,
    DocumentOwnerType OwnerType,
    Guid? OwnerId,
    string? OwnerName,
    Guid DocumentTypeId,
    string TypeName,
    DocumentCategory Category,
    string Title,
    int CurrentVersion,
    DateOnly? ExpiresOn,
    bool IsExpired,
    bool EmployeeVisible,
    DocumentSource Source,
    bool IsActive,
    DateTime CreatedDate,
    DateTime? ModifiedDate);

public sealed record VersionItem(int Number, string FileName, string ContentType, long SizeBytes, string? Note, DateTime CreatedDate);

public sealed record SignatureItem(
    Guid Id,
    Guid DocumentId,
    string DocumentTitle,
    int VersionNumber,
    Guid SignerEmployeeId,
    string? SignerName,
    SignatureStatus Status,
    string Provider,
    DateTime RequestedDate,
    DateTime? CompletedDate,
    string? Comment);

public sealed record DocumentDetail(DocumentItem Document, IReadOnlyList<VersionItem> Versions, IReadOnlyList<SignatureItem> Signatures);

/// <summary>A file on its way in: what the browser sent, before anything is trusted.</summary>
public sealed record IncomingFile(string FileName, long Length, Func<Stream> Open);

public sealed record DocumentListQuery
{
    public DocumentOwnerType? OwnerType { get; init; }

    public Guid? OwnerId { get; init; }

    public Guid? DocumentTypeId { get; init; }

    public string? Search { get; init; }

    /// <summary>Only documents expiring within this many days (including already expired).</summary>
    public int? ExpiringWithinDays { get; init; }

    public bool? IncludeArchived { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

/// <summary>
/// Documents (spec §107): who may see which, storing versions, filling templates in and collecting
/// signatures. Employee documents follow the employee data scope - a manager with documents at
/// direct-reports scope sees their team's files and no one else's.
/// </summary>
internal sealed partial class DocumentService(
    AppDbContext dbContext,
    IFileStorage fileStorage,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ISignatureProvider signatureProvider,
    IEnumerable<IDocumentOwnerAccess> ownerAccess,
    TimeProvider timeProvider)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>What may be uploaded, and the content type it is served back with - never the browser's claim.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
    };

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<DocumentItem>> ListAsync(DocumentListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Document> documents = dbContext.Documents.AsNoTracking();

        if (query.OwnerType == DocumentOwnerType.Employee && query.OwnerId is { } ownerId)
        {
            if (!await employees.IsInScopeAsync(ownerId, Permissions.Document.View, cancellationToken))
            {
                throw new NotFoundException("Employee", ownerId);
            }

            documents = documents.Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == ownerId);
        }
        else if (query.OwnerType is { } external && IsExternal(external) && query.OwnerId is { } externalId)
        {
            if (!await AccessFor(external).CanReadAsync(externalId, cancellationToken))
            {
                throw new NotFoundException(external.ToString(), externalId);
            }

            documents = documents.Where(document => document.OwnerType == external && document.OwnerId == externalId);
        }
        else
        {
            // Browsing beyond one person's documents is the whole library: tenant-wide grants only.
            await RequireTenantScopeAsync(Permissions.Document.View, cancellationToken);
            if (query.OwnerType is { } ownerType)
            {
                documents = documents.Where(document => document.OwnerType == ownerType);
            }
        }

        if (query.IncludeArchived != true)
        {
            documents = documents.Where(document => document.IsActive);
        }

        if (query.DocumentTypeId is { } typeId)
        {
            documents = documents.Where(document => document.DocumentTypeId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            documents = documents.Where(document => document.Title.Contains(search));
        }

        if (query.ExpiringWithinDays is { } days)
        {
            DateOnly limit = DateOnly.FromDateTime(Now).AddDays(Math.Clamp(days, 0, 3650));
            documents = documents.Where(document => document.ExpiresOn != null && document.ExpiresOn <= limit);
        }

        PageRequest page = query.ToPageRequest();
        long total = await documents.LongCountAsync(cancellationToken);
        List<Document> rows = await documents
            .OrderByDescending(document => document.ModifiedDate ?? document.CreatedDate)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult.From(await ToItemsAsync(rows, cancellationToken), page, total);
    }

    public async Task<DocumentDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Document document = await FindAsync(id, Permissions.Document.View, cancellationToken);
        return await DetailAsync(document, cancellationToken);
    }

    public async Task<DocumentDetail> UploadAsync(
        DocumentOwnerType ownerType,
        Guid? ownerId,
        Guid documentTypeId,
        string title,
        DateOnly? expiresOn,
        bool? employeeVisible,
        string? note,
        IncomingFile file,
        CancellationToken cancellationToken)
    {
        await EnsureOwnerAccessAsync(ownerType, ownerId, Permissions.Document.Upload, cancellationToken);
        DocumentType type = await ActiveTypeAsync(documentTypeId, cancellationToken);

        var document = Document.Create(ownerType, ownerId, type, title, expiresOn, employeeVisible, DocumentSource.Uploaded);
        dbContext.Documents.Add(document);

        (string contentType, string extension) = Inspect(file);
        await using Stream content = file.Open();
        await StoreVersionAsync(document, Path.GetFileName(file.FileName), contentType, extension, content, note, cancellationToken);

        return await DetailAsync(document, cancellationToken);
    }

    public async Task<DocumentDetail> AddVersionAsync(Guid id, string? note, IncomingFile file, CancellationToken cancellationToken)
    {
        Document document = await FindAsync(id, Permissions.Document.Upload, cancellationToken, track: true);
        if (!document.IsActive)
        {
            throw new DomainRuleException("DOCUMENT_ARCHIVED", "Restore the document before adding a version.");
        }

        (string contentType, string extension) = Inspect(file);
        await using Stream content = file.Open();
        await StoreVersionAsync(document, Path.GetFileName(file.FileName), contentType, extension, content, note, cancellationToken);

        return await DetailAsync(document, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenAsync(Guid id, int number, CancellationToken cancellationToken)
    {
        Document document = await FindAsync(id, Permissions.Document.View, cancellationToken);
        return await OpenVersionAsync(document, number, cancellationToken);
    }

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken)
    {
        Document document = await FindAsync(id, Permissions.Document.Delete, cancellationToken, track: true);
        if (archived)
        {
            document.Archive();
        }
        else
        {
            document.Restore();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Fills a template in for one employee and stores it as a new document.</summary>
    public async Task<DocumentDetail> GenerateAsync(Guid templateId, Guid employeeId, string? title, Guid? signerEmployeeId, CancellationToken cancellationToken)
    {
        await EnsureOwnerAccessAsync(DocumentOwnerType.Employee, employeeId, Permissions.Document.Upload, cancellationToken);

        DocumentTemplate template = await dbContext.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(row => row.Id == templateId && row.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentTemplate), templateId);
        return await DetailAsync(await GenerateFromAsync(template, employeeId, title, signerEmployeeId, new Dictionary<string, string>(), cancellationToken), cancellationToken);
    }

    /// <summary>Generates a letter by template code for another module; its access was checked there.</summary>
    internal async Task<Guid> GenerateByCodeAsync(Guid employeeId, string templateCode, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        DocumentTemplate template = await dbContext.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(row => row.Code == templateCode && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("TEMPLATE_MISSING", $"There is no active letter template {templateCode}.");
        return (await GenerateFromAsync(template, employeeId, null, null, values, cancellationToken)).Id;
    }

    private async Task<Document> GenerateFromAsync(DocumentTemplate template, Guid employeeId, string? title, Guid? signerEmployeeId, IReadOnlyDictionary<string, string> extra, CancellationToken cancellationToken)
    {
        DocumentType type = await ActiveTypeAsync(template.DocumentTypeId, cancellationToken);
        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

        string department = person.DepartmentId is { } departmentId
            ? (await orgStructure.GetNamesAsync(OrgUnitKind.Department, [departmentId], cancellationToken)).GetValueOrDefault(departmentId) ?? string.Empty
            : string.Empty;

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["employeeName"] = person.DisplayName,
            ["employeeCode"] = person.EmployeeCode,
            ["officialEmail"] = person.OfficialEmail ?? string.Empty,
            ["designation"] = person.Designation ?? string.Empty,
            ["department"] = department,
            ["joiningDate"] = person.JoiningDate?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
            ["today"] = DateOnly.FromDateTime(Now).ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        };
        foreach ((string key, string value) in extra)
        {
            values[key] = value;
        }

        string html = FillTemplate(template.Body, values);
        string documentTitle = string.IsNullOrWhiteSpace(title) ? $"{template.Name} - {person.DisplayName}" : title;

        var document = Document.Create(DocumentOwnerType.Employee, employeeId, type, documentTitle, null, null, DocumentSource.Generated);
        dbContext.Documents.Add(document);

        string page = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(documentTitle)}</title></head><body>{html}</body></html>";
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(page));
        string fileName = $"{SafeFileName(documentTitle)}.html";
        await StoreVersionAsync(document, fileName, "text/html", ".html", content, $"Generated from {template.Name}", cancellationToken, save: signerEmployeeId is null);

        if (signerEmployeeId is { } signer)
        {
            await AddSignatureRequestAsync(document, signer, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return document;
    }

    public async Task<SignatureItem> RequestSignatureAsync(Guid documentId, Guid signerEmployeeId, CancellationToken cancellationToken)
    {
        Document document = await FindAsync(documentId, Permissions.Document.Upload, cancellationToken);
        DocumentSignature request = await AddSignatureRequestAsync(document, signerEmployeeId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToSignatureItemsAsync([request], cancellationToken))[0];
    }

    public async Task CancelSignatureAsync(Guid requestId, CancellationToken cancellationToken)
    {
        DocumentSignature request = await dbContext.DocumentSignatures.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentSignature), requestId);
        await FindAsync(request.DocumentId, Permissions.Document.Upload, cancellationToken);

        request.Cancel(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Self-service -------------------------------------------------------------------------

    /// <summary>My own documents HR has made visible to me, plus anything I have been asked to sign.</summary>
    public async Task<IReadOnlyList<DocumentItem>> MyDocumentsAsync(CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        List<Guid> toSign = await dbContext.DocumentSignatures.AsNoTracking()
            .Where(request => request.SignerEmployeeId == me)
            .Select(request => request.DocumentId)
            .ToListAsync(cancellationToken);

        List<Document> rows = await dbContext.Documents.AsNoTracking()
            .Where(document => document.IsActive
                && ((document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == me && document.EmployeeVisible) || toSign.Contains(document.Id)))
            .OrderByDescending(document => document.ModifiedDate ?? document.CreatedDate)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await ToItemsAsync(rows, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenMineAsync(Guid id, int number, CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        Document document = await dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id && row.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), id);

        bool mine = document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == me && document.EmployeeVisible;
        bool toSign = await dbContext.DocumentSignatures.AnyAsync(request => request.DocumentId == id && request.SignerEmployeeId == me, cancellationToken);
        if (!mine && !toSign)
        {
            throw new NotFoundException(nameof(Document), id);
        }

        return await OpenVersionAsync(document, number, cancellationToken);
    }

    public async Task<IReadOnlyList<SignatureItem>> MySignatureRequestsAsync(CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        List<DocumentSignature> requests = await dbContext.DocumentSignatures.AsNoTracking()
            .Where(request => request.SignerEmployeeId == me)
            .OrderBy(request => request.Status)
            .ThenByDescending(request => request.RequestedDate)
            .Take(200)
            .ToListAsync(cancellationToken);

        return await ToSignatureItemsAsync(requests, cancellationToken);
    }

    public async Task SignAsync(Guid requestId, bool sign, string? comment, CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        DocumentSignature request = await dbContext.DocumentSignatures.FirstOrDefaultAsync(row => row.Id == requestId && row.SignerEmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentSignature), requestId);

        if (sign)
        {
            request.Sign(Now, currentUser.IpAddress);
            publisher.Publish(new DocumentSignedIntegrationEvent(request.DocumentId, request.VersionNumber, me, Now));
        }
        else
        {
            request.Decline(Now, comment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<DocumentSignature> AddSignatureRequestAsync(Document document, Guid signerEmployeeId, CancellationToken cancellationToken)
    {
        if (!await employees.IsActiveAsync(signerEmployeeId, cancellationToken))
        {
            throw new DomainRuleException("SIGNATURE_SIGNER_INACTIVE", "Only a current employee can be asked to sign.");
        }

        bool alreadyOpen = await dbContext.DocumentSignatures.AnyAsync(
            request => request.DocumentId == document.Id && request.SignerEmployeeId == signerEmployeeId && request.Status == SignatureStatus.Requested,
            cancellationToken);
        if (alreadyOpen)
        {
            throw new DomainRuleException("SIGNATURE_ALREADY_REQUESTED", "That person has already been asked to sign this document.");
        }

        var request = DocumentSignature.Create(document.Id, document.CurrentVersion, signerEmployeeId, signatureProvider.Name, Now);
        request.AttachReference(await signatureProvider.RequestAsync(request, cancellationToken));
        dbContext.DocumentSignatures.Add(request);

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.DocumentSignatureRequested,
                [new NotificationRecipient(signerEmployeeId)],
                new Dictionary<string, string> { ["documentTitle"] = document.Title },
                "/me/documents"),
            cancellationToken);

        return request;
    }

    private async Task StoreVersionAsync(
        Document document,
        string fileName,
        string contentType,
        string extension,
        Stream content,
        string? note,
        CancellationToken cancellationToken,
        bool save = true)
    {
        int number = document.NextVersion();
        StoredFile stored = await fileStorage.SaveAsync(
            new FileToStore
            {
                Content = content,
                RelativeKey = $"documents/{document.Id:N}/v{number}{extension.ToLowerInvariant()}",
                ContentType = contentType,
            },
            cancellationToken);

        dbContext.DocumentVersions.Add(DocumentVersion.Create(document.Id, number, fileName, contentType, stored.SizeBytes, stored.Sha256, stored.StorageKey, note));

        if (!save)
        {
            return;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The row did not commit, so nothing points at the file: remove it rather than leave an orphan.
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
    }

    private async Task<(Stream Content, string ContentType, string FileName)> OpenVersionAsync(Document document, int number, CancellationToken cancellationToken)
    {
        DocumentVersion version = await dbContext.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.DocumentId == document.Id && row.Number == number, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentVersion), number);

        return (await fileStorage.OpenReadAsync(version.StorageKey, cancellationToken), version.ContentType, version.FileName);
    }

    /// <summary>Checks a file against the allow-list and its magic bytes, and decides its content type.</summary>
    private static (string ContentType, string Extension) Inspect(IncomingFile file)
    {
        if (file.Length <= 0)
        {
            throw new DomainRuleException("DOCUMENT_EMPTY", "The file is empty.");
        }

        if (file.Length > MaxFileBytes)
        {
            throw new DomainRuleException("DOCUMENT_TOO_LARGE", "Files can be at most 10 MB.");
        }

        string extension = Path.GetExtension(file.FileName);
        if (!AllowedTypes.TryGetValue(extension, out string? contentType))
        {
            throw new DomainRuleException("DOCUMENT_TYPE_NOT_ALLOWED", "Upload a PDF, image, Office document, text or CSV file.");
        }

        byte[] head = new byte[8];
        int read;
        using (Stream peek = file.Open())
        {
            read = peek.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        bool matches = extension.ToLowerInvariant() switch
        {
            ".pdf" => read >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46,
            ".png" => read >= 4 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
            ".jpg" or ".jpeg" => read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            ".docx" or ".xlsx" => read >= 2 && head[0] == 0x50 && head[1] == 0x4B,
            _ => true,
        };

        if (!matches)
        {
            throw new DomainRuleException("DOCUMENT_CONTENT_MISMATCH", "The file's contents do not match its extension.");
        }

        return (contentType, extension);
    }

    private async Task<Document> FindAsync(Guid id, string permission, CancellationToken cancellationToken, bool track = false)
    {
        IQueryable<Document> source = track ? dbContext.Documents : dbContext.Documents.AsNoTracking();
        Document document = await source.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), id);

        bool allowed = document.OwnerType switch
        {
            DocumentOwnerType.Employee => await employees.IsInScopeAsync(document.OwnerId!.Value, permission, cancellationToken),
            DocumentOwnerType.Client or DocumentOwnerType.Project => permission == Permissions.Document.View
                ? await AccessFor(document.OwnerType).CanReadAsync(document.OwnerId!.Value, cancellationToken)
                : await AccessFor(document.OwnerType).CanWriteAsync(document.OwnerId!.Value, cancellationToken),
            _ => await HasTenantScopeAsync(permission, cancellationToken),
        };

        return allowed ? document : throw new NotFoundException(nameof(Document), id);
    }

    private async Task EnsureOwnerAccessAsync(DocumentOwnerType ownerType, Guid? ownerId, string permission, CancellationToken cancellationToken)
    {
        if (ownerType == DocumentOwnerType.Employee)
        {
            if (ownerId is not { } employeeId || !await employees.IsInScopeAsync(employeeId, permission, cancellationToken))
            {
                throw new NotFoundException("Employee", ownerId ?? Guid.Empty);
            }

            return;
        }

        if (IsExternal(ownerType))
        {
            if (ownerId is not { } externalId || !await AccessFor(ownerType).CanWriteAsync(externalId, cancellationToken))
            {
                throw new NotFoundException(ownerType.ToString(), ownerId ?? Guid.Empty);
            }

            return;
        }

        await RequireTenantScopeAsync(permission, cancellationToken);
    }

    private static bool IsExternal(DocumentOwnerType ownerType) => ownerType is DocumentOwnerType.Client or DocumentOwnerType.Project;

    /// <summary>The owning module's access rules; a kind of owner nobody answers for is closed.</summary>
    private IDocumentOwnerAccess AccessFor(DocumentOwnerType ownerType)
        => ownerAccess.FirstOrDefault(access => string.Equals(access.OwnerType, ownerType.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ForbiddenException("DOCUMENT_OWNER_CLOSED", $"Documents cannot be held for a {ownerType.ToString().ToLowerInvariant()} here.");

    private async Task RequireTenantScopeAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await HasTenantScopeAsync(permission, cancellationToken))
        {
            throw new ForbiddenException("DOCUMENT_SCOPE", "This needs document access across the whole organization.");
        }
    }

    private async Task<bool> HasTenantScopeAsync(string permission, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).ScopeFor(permission) == DataScope.Tenant;

    private async Task<DocumentType> ActiveTypeAsync(Guid typeId, CancellationToken cancellationToken)
        => await dbContext.DocumentTypes.FirstOrDefaultAsync(type => type.Id == typeId && type.IsActive, cancellationToken)
            ?? throw new DomainRuleException("DOCUMENT_TYPE_UNKNOWN", "Choose an active document type.");

    private Guid RequireEmployee()
        => currentUser.EmployeeId ?? throw new DomainRuleException("DOCUMENT_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<DocumentDetail> DetailAsync(Document document, CancellationToken cancellationToken)
    {
        List<DocumentVersion> versions = await dbContext.DocumentVersions.AsNoTracking()
            .Where(version => version.DocumentId == document.Id)
            .OrderByDescending(version => version.Number)
            .ToListAsync(cancellationToken);

        List<DocumentSignature> signatures = await dbContext.DocumentSignatures.AsNoTracking()
            .Where(request => request.DocumentId == document.Id)
            .OrderByDescending(request => request.RequestedDate)
            .ToListAsync(cancellationToken);

        return new DocumentDetail(
            (await ToItemsAsync([document], cancellationToken))[0],
            [.. versions.Select(version => new VersionItem(version.Number, version.FileName, version.ContentType, version.SizeBytes, version.Note, version.CreatedDate))],
            await ToSignatureItemsAsync(signatures, cancellationToken));
    }

    private async Task<List<DocumentItem>> ToItemsAsync(List<Document> documents, CancellationToken cancellationToken)
    {
        List<Guid> typeIds = [.. documents.Select(document => document.DocumentTypeId).Distinct()];
        Dictionary<Guid, DocumentType> types = await dbContext.DocumentTypes.AsNoTracking()
            .Where(type => typeIds.Contains(type.Id))
            .ToDictionaryAsync(type => type.Id, cancellationToken);

        Dictionary<Guid, string> owners = (await employees.GetManyAsync(
                documents.Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId is not null).Select(document => document.OwnerId!.Value),
                cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        foreach (IGrouping<DocumentOwnerType, Document> external in documents.Where(document => IsExternal(document.OwnerType) && document.OwnerId is not null).GroupBy(document => document.OwnerType))
        {
            IDocumentOwnerAccess? access = ownerAccess.FirstOrDefault(candidate => string.Equals(candidate.OwnerType, external.Key.ToString(), StringComparison.OrdinalIgnoreCase));
            if (access is not null)
            {
                foreach ((Guid id, string name) in await access.GetNamesAsync(external.Select(document => document.OwnerId!.Value), cancellationToken))
                {
                    owners.TryAdd(id, name);
                }
            }
        }

        DateOnly today = DateOnly.FromDateTime(Now);
        return [.. documents.Select(document =>
        {
            DocumentType? type = types.GetValueOrDefault(document.DocumentTypeId);
            return new DocumentItem(
                document.Id,
                document.OwnerType,
                document.OwnerId,
                document.OwnerId is { } owner ? owners.GetValueOrDefault(owner) : null,
                document.DocumentTypeId,
                type?.Name ?? string.Empty,
                type?.Category ?? DocumentCategory.Other,
                document.Title,
                document.CurrentVersion,
                document.ExpiresOn,
                document.ExpiresOn is { } expires && expires < today,
                document.EmployeeVisible,
                document.Source,
                document.IsActive,
                document.CreatedDate,
                document.ModifiedDate);
        })];
    }

    private async Task<List<SignatureItem>> ToSignatureItemsAsync(List<DocumentSignature> requests, CancellationToken cancellationToken)
    {
        List<Guid> documentIds = [.. requests.Select(request => request.DocumentId).Distinct()];
        Dictionary<Guid, string> titles = await dbContext.Documents.AsNoTracking()
            .Where(document => documentIds.Contains(document.Id))
            .ToDictionaryAsync(document => document.Id, document => document.Title, cancellationToken);

        // Requests created in this unit of work are not in the database yet.
        foreach (Document pending in dbContext.ChangeTracker.Entries<Document>().Select(entry => entry.Entity))
        {
            titles.TryAdd(pending.Id, pending.Title);
        }

        Dictionary<Guid, string> signers = (await employees.GetManyAsync(requests.Select(request => request.SignerEmployeeId), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. requests.Select(request => new SignatureItem(
            request.Id,
            request.DocumentId,
            titles.GetValueOrDefault(request.DocumentId) ?? string.Empty,
            request.VersionNumber,
            request.SignerEmployeeId,
            signers.GetValueOrDefault(request.SignerEmployeeId),
            request.Status,
            request.Provider,
            request.RequestedDate,
            request.CompletedDate,
            request.Comment))];
    }

    /// <summary>Placeholders filled with HTML-encoded values: an employee's name cannot inject markup into a letter.</summary>
    internal static string FillTemplate(string body, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(body, match => values.TryGetValue(match.Groups[1].Value, out string? value) ? WebUtility.HtmlEncode(value) : string.Empty);

    private static string SafeFileName(string title)
    {
        string cleaned = UnsafeFileNameCharacters().Replace(title, "-").Trim('-');
        return cleaned.Length == 0 ? "document" : cleaned[..Math.Min(cleaned.Length, 80)];
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[^A-Za-z0-9 _.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileNameCharacters();
}
