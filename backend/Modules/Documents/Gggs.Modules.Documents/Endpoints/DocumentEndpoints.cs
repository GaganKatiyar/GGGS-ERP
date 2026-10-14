using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Documents.Application;
using Gggs.Modules.Documents.Domain;
using Gggs.Modules.Documents.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Documents.Endpoints;

public sealed record DocumentTypeItem(Guid Id, string Code, string Name, DocumentCategory Category, bool EmployeeVisible, bool RequiresExpiry, bool IsSystem, bool IsActive);

public sealed record DocumentTypeRequest(string Code, string Name, DocumentCategory Category, bool EmployeeVisible, bool RequiresExpiry);

public sealed record DocumentTemplateItem(Guid Id, string Code, string Name, string? Description, Guid DocumentTypeId, string TypeName, string Body, bool IsActive);

public sealed record DocumentTemplateRequest(string Code, string Name, string? Description, Guid DocumentTypeId, string Body);

public sealed record GenerateDocumentRequest(Guid EmployeeId, string? Title, Guid? SignerEmployeeId);

public sealed record SignatureRequestBody(Guid SignerEmployeeId);

public sealed record SignatureDecisionBody(string? Comment);

/// <summary>Documents (spec §107), and the employee's own view of theirs under <c>/api/v1/me</c>.</summary>
internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/documents").WithTags("Documents");

        group.MapGet("/types", ListTypesAsync).WithName("ListDocumentTypes")
            .RequirePermission(Permissions.Document.View, Permissions.Document.Upload, Permissions.Document.TypeManage);
        group.MapPost("/types", CreateTypeAsync).WithName("CreateDocumentType").RequirePermission(Permissions.Document.TypeManage);
        group.MapPut("/types/{id:guid}", UpdateTypeAsync).WithName("UpdateDocumentType").RequirePermission(Permissions.Document.TypeManage);
        group.MapPost("/types/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => SetTypeActiveAsync(id, true, db, ct))
            .WithName("ActivateDocumentType").RequirePermission(Permissions.Document.TypeManage);
        group.MapPost("/types/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => SetTypeActiveAsync(id, false, db, ct))
            .WithName("DeactivateDocumentType").RequirePermission(Permissions.Document.TypeManage);

        group.MapGet("/templates", ListTemplatesAsync).WithName("ListDocumentTemplates").RequirePermission(Permissions.Document.TemplateManage);
        group.MapPost("/templates", CreateTemplateAsync).WithName("CreateDocumentTemplate").RequirePermission(Permissions.Document.TemplateManage);
        group.MapPut("/templates/{id:guid}", UpdateTemplateAsync).WithName("UpdateDocumentTemplate").RequirePermission(Permissions.Document.TemplateManage);
        group.MapPost("/templates/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => SetTemplateActiveAsync(id, true, db, ct))
            .WithName("ActivateDocumentTemplate").RequirePermission(Permissions.Document.TemplateManage);
        group.MapPost("/templates/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => SetTemplateActiveAsync(id, false, db, ct))
            .WithName("DeactivateDocumentTemplate").RequirePermission(Permissions.Document.TemplateManage);
        group.MapPost("/templates/{id:guid}/generate", async (Guid id, GenerateDocumentRequest body, DocumentService documents, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                DocumentDetail created = await documents.GenerateAsync(id, body.EmployeeId, body.Title, body.SignerEmployeeId, ct);
                return TypedResults.Created($"/api/v1/documents/{created.Document.Id}", created);
            })
            .WithName("GenerateDocument").RequirePermission(Permissions.Document.TemplateManage);

        group.MapGet("/", async ([AsParameters] DocumentListQuery query, DocumentService documents, CancellationToken ct) => TypedResults.Ok(await documents.ListAsync(query, ct)))
            .WithName("ListDocuments").RequirePermission(Permissions.Document.View);
        group.MapGet("/{id:guid}", async (Guid id, DocumentService documents, CancellationToken ct) => TypedResults.Ok(await documents.GetAsync(id, ct)))
            .WithName("GetDocument").RequirePermission(Permissions.Document.View);

        // Multipart uploads. Antiforgery tokens are not used in this API: cookie requests are guarded
        // by the X-CSRF header check instead (BrowserRequestGuard), which covers forms too.
        group.MapPost("/", UploadAsync).WithName("UploadDocument").RequirePermission(Permissions.Document.Upload).DisableAntiforgery();
        group.MapPost("/{id:guid}/versions", AddVersionAsync).WithName("AddDocumentVersion").RequirePermission(Permissions.Document.Upload).DisableAntiforgery();

        group.MapGet("/{id:guid}/versions/{number:int}/content", async (Guid id, int number, DocumentService documents, CancellationToken ct) =>
            {
                (Stream content, string contentType, string fileName) = await documents.OpenAsync(id, number, ct);
                return TypedResults.File(content, contentType, fileName);
            })
            .WithName("DownloadDocumentVersion").RequirePermission(Permissions.Document.View);

        group.MapPost("/{id:guid}/archive", async (Guid id, DocumentService documents, CancellationToken ct) =>
            {
                await documents.SetArchivedAsync(id, archived: true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ArchiveDocument").RequirePermission(Permissions.Document.Delete);
        group.MapPost("/{id:guid}/restore", async (Guid id, DocumentService documents, CancellationToken ct) =>
            {
                await documents.SetArchivedAsync(id, archived: false, ct);
                return TypedResults.NoContent();
            })
            .WithName("RestoreDocument").RequirePermission(Permissions.Document.Delete);

        group.MapPost("/{id:guid}/signature-requests", async (Guid id, SignatureRequestBody body, DocumentService documents, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                return TypedResults.Ok(await documents.RequestSignatureAsync(id, body.SignerEmployeeId, ct));
            })
            .WithName("RequestDocumentSignature").RequirePermission(Permissions.Document.Upload);
        group.MapPost("/signature-requests/{id:guid}/cancel", async (Guid id, DocumentService documents, CancellationToken ct) =>
            {
                await documents.CancelSignatureAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelDocumentSignature").RequirePermission(Permissions.Document.Upload);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Documents");
        me.MapGet("/documents", async (DocumentService documents, CancellationToken ct) => TypedResults.Ok(await documents.MyDocumentsAsync(ct)))
            .WithName("ListMyDocuments").RequirePermission(Permissions.Self.DocumentView);
        me.MapGet("/documents/{id:guid}/versions/{number:int}/content", async (Guid id, int number, DocumentService documents, CancellationToken ct) =>
            {
                (Stream content, string contentType, string fileName) = await documents.OpenMineAsync(id, number, ct);
                return TypedResults.File(content, contentType, fileName);
            })
            .WithName("DownloadMyDocument").RequirePermission(Permissions.Self.DocumentView);
        me.MapGet("/signature-requests", async (DocumentService documents, CancellationToken ct) => TypedResults.Ok(await documents.MySignatureRequestsAsync(ct)))
            .WithName("ListMySignatureRequests").RequirePermission(Permissions.Self.DocumentView);
        me.MapPost("/signature-requests/{id:guid}/sign", async (Guid id, DocumentService documents, CancellationToken ct) =>
            {
                await documents.SignAsync(id, sign: true, comment: null, ct);
                return TypedResults.NoContent();
            })
            .WithName("SignDocument").RequirePermission(Permissions.Self.DocumentView);
        me.MapPost("/signature-requests/{id:guid}/decline", async (Guid id, SignatureDecisionBody? body, DocumentService documents, CancellationToken ct) =>
            {
                await documents.SignAsync(id, sign: false, body?.Comment, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeclineDocumentSignature").RequirePermission(Permissions.Self.DocumentView);

        return endpoints;
    }

    private static async Task<Created<DocumentDetail>> UploadAsync(
        [FromForm] DocumentOwnerType ownerType,
        [FromForm] Guid? ownerId,
        [FromForm] Guid documentTypeId,
        [FromForm] string title,
        [FromForm] DateOnly? expiresOn,
        [FromForm] bool? employeeVisible,
        [FromForm] string? note,
        IFormFile file,
        DocumentService documents,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        DocumentDetail created = await documents.UploadAsync(
            ownerType, ownerId, documentTypeId, title, expiresOn, employeeVisible, note,
            new IncomingFile(file.FileName, file.Length, file.OpenReadStream), ct);

        return TypedResults.Created($"/api/v1/documents/{created.Document.Id}", created);
    }

    private static async Task<Ok<DocumentDetail>> AddVersionAsync(
        Guid id,
        [FromForm] string? note,
        IFormFile file,
        DocumentService documents,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        return TypedResults.Ok(await documents.AddVersionAsync(id, note, new IncomingFile(file.FileName, file.Length, file.OpenReadStream), ct));
    }

    private static async Task<Ok<List<DocumentTypeItem>>> ListTypesAsync(bool? includeInactive, AppDbContext database, CancellationToken ct)
        => TypedResults.Ok(await database.DocumentTypes.AsNoTracking()
            .Where(type => includeInactive == true || type.IsActive)
            .OrderBy(type => type.Category).ThenBy(type => type.Name)
            .Select(type => new DocumentTypeItem(type.Id, type.Code, type.Name, type.Category, type.EmployeeVisible, type.RequiresExpiry, type.IsSystem, type.IsActive))
            .ToListAsync(ct));

    private static async Task<Created<DocumentTypeItem>> CreateTypeAsync(DocumentTypeRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Code.Trim().ToUpperInvariant();
        if (await database.DocumentTypes.AnyAsync(type => type.Code == code, ct))
        {
            throw new DomainRuleException("DOCUMENT_TYPE_CODE_IN_USE", $"A document type with code '{code}' already exists.");
        }

        var type = DocumentType.Create(code, body.Name, body.Category, body.EmployeeVisible, body.RequiresExpiry);
        database.DocumentTypes.Add(type);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/documents/types/{type.Id}", ToItem(type));
    }

    private static async Task<Ok<DocumentTypeItem>> UpdateTypeAsync(Guid id, DocumentTypeRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        DocumentType type = await database.DocumentTypes.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(DocumentType), id);
        type.Update(body.Name, body.Category, body.EmployeeVisible, body.RequiresExpiry);
        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(ToItem(type));
    }

    private static async Task<Ok<DocumentTypeItem>> SetTypeActiveAsync(Guid id, bool active, AppDbContext database, CancellationToken ct)
    {
        DocumentType type = await database.DocumentTypes.FirstOrDefaultAsync(row => row.Id == id, ct) ?? throw new NotFoundException(nameof(DocumentType), id);
        if (active)
        {
            type.Activate();
        }
        else
        {
            type.Deactivate();
        }

        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(ToItem(type));
    }

    private static async Task<Ok<List<DocumentTemplateItem>>> ListTemplatesAsync(AppDbContext database, CancellationToken ct)
        => TypedResults.Ok(await database.DocumentTemplates.AsNoTracking()
            .Join(database.DocumentTypes.AsNoTracking(), template => template.DocumentTypeId, type => type.Id, (template, type) => new { template, type.Name })
            .OrderBy(row => row.template.Name)
            .Select(row => new DocumentTemplateItem(
                row.template.Id, row.template.Code, row.template.Name, row.template.Description, row.template.DocumentTypeId, row.Name, row.template.Body, row.template.IsActive))
            .ToListAsync(ct));

    private static async Task<Created<DocumentTemplateItem>> CreateTemplateAsync(DocumentTemplateRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Code.Trim().ToUpperInvariant();
        if (await database.DocumentTemplates.AnyAsync(template => template.Code == code, ct))
        {
            throw new DomainRuleException("DOCUMENT_TEMPLATE_CODE_IN_USE", $"A template with code '{code}' already exists.");
        }

        DocumentType type = await RequireTypeAsync(database, body.DocumentTypeId, ct);
        var template = DocumentTemplate.Create(code, body.Name, body.Description, type.Id, body.Body);
        database.DocumentTemplates.Add(template);
        await database.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/documents/templates/{template.Id}", ToItem(template, type.Name));
    }

    private static async Task<Ok<DocumentTemplateItem>> UpdateTemplateAsync(Guid id, DocumentTemplateRequest body, AppDbContext database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        DocumentTemplate template = await database.DocumentTemplates.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new NotFoundException(nameof(DocumentTemplate), id);
        DocumentType type = await RequireTypeAsync(database, body.DocumentTypeId, ct);

        template.Update(body.Name, body.Description, type.Id, body.Body);
        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(ToItem(template, type.Name));
    }

    private static async Task<NoContent> SetTemplateActiveAsync(Guid id, bool active, AppDbContext database, CancellationToken ct)
    {
        DocumentTemplate template = await database.DocumentTemplates.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new NotFoundException(nameof(DocumentTemplate), id);
        if (active)
        {
            template.Activate();
        }
        else
        {
            template.Deactivate();
        }

        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<DocumentType> RequireTypeAsync(AppDbContext database, Guid typeId, CancellationToken ct)
        => await database.DocumentTypes.FirstOrDefaultAsync(type => type.Id == typeId && type.IsActive, ct)
            ?? throw new DomainRuleException("DOCUMENT_TYPE_UNKNOWN", "Choose an active document type.");

    private static DocumentTypeItem ToItem(DocumentType type)
        => new(type.Id, type.Code, type.Name, type.Category, type.EmployeeVisible, type.RequiresExpiry, type.IsSystem, type.IsActive);

    private static DocumentTemplateItem ToItem(DocumentTemplate template, string typeName)
        => new(template.Id, template.Code, template.Name, template.Description, template.DocumentTypeId, typeName, template.Body, template.IsActive);
}
