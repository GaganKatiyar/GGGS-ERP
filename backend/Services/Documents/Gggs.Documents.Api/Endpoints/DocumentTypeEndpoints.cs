using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Documents.Api.Contracts;
using Gggs.Documents.Application.Documents;
using Gggs.Documents.Domain.Documents;
using Gggs.Documents.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Documents.Api.Endpoints;

/// <summary>Administering the catalog of document types a tenant can file uploads under.</summary>
public static class DocumentTypeEndpoints
{
    public static IEndpointRouteBuilder MapDocumentTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/documents/types").WithTags("Document Types");

        group.MapGet("/", ListAsync).WithName("ListDocumentTypes").RequirePermission(Permissions.Document.View);
        group.MapPost("/", CreateAsync).WithName("CreateDocumentType").RequirePermission(Permissions.Document.TypeManage);
        group.MapPut("/{id:guid}/rules", UpdateRulesAsync)
            .WithName("UpdateDocumentTypeRules")
            .RequirePermission(Permissions.Document.TypeManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<DocumentTypeResponse>>> ListAsync(
        DocumentDbContext database,
        CancellationToken cancellationToken)
    {
        List<DocumentType> types = await database.DocumentTypes.AsNoTracking()
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<DocumentTypeResponse>>([.. types.Select(ToResponse)]);
    }

    private static async Task<Created<DocumentTypeResponse>> CreateAsync(
        CreateDocumentTypeBody body,
        DocumentTypeService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        DocumentType created = await service.CreateAsync(
            body.Code, body.Name, body.Category, body.AllowedExtensions, body.MaxSizeMb,
            body.IsEmployeeUploadable, body.IsSensitive, cancellationToken);

        DocumentTypeResponse response = ToResponse(created);
        return TypedResults.Created($"/api/v1/documents/types/{created.Id}", response);
    }

    private static async Task<Ok<DocumentTypeResponse>> UpdateRulesAsync(
        Guid id,
        UpdateDocumentTypeRulesBody body,
        DocumentTypeService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        DocumentType updated = await service.UpdateRulesAsync(
            id, body.AllowedExtensions, body.MaxSizeMb, body.IsEmployeeUploadable, body.IsSensitive, cancellationToken);

        return TypedResults.Ok(ToResponse(updated));
    }

    private static DocumentTypeResponse ToResponse(DocumentType type) => new(
        type.Id,
        type.Code,
        type.Name,
        type.Category,
        type.AllowedExtensionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
        type.MaxSizeMb,
        type.IsEmployeeUploadable,
        type.IsSensitive);
}
