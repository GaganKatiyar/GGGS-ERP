using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Documents.Domain.Documents;

namespace Gggs.Documents.Application.Documents;

/// <summary>Administering the catalog of document types a tenant can file uploads under.</summary>
public sealed class DocumentTypeService(IDocumentTypeRepository types, IUnitOfWork unitOfWork)
{
    public async Task<DocumentType> CreateAsync(
        string code,
        string name,
        DocumentCategory category,
        IReadOnlyCollection<string> allowedExtensions,
        int maxSizeMb,
        bool isEmployeeUploadable,
        bool isSensitive,
        CancellationToken cancellationToken = default)
    {
        string normalizedCode = DocumentType.NormalizeCode(code);

        if (await types.CodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new DomainRuleException("DOCUMENT_TYPE_CODE_IN_USE", $"A document type with code '{normalizedCode}' already exists.");
        }

        DocumentType documentType = DocumentType.Create(
            code, name, category, allowedExtensions, maxSizeMb, isEmployeeUploadable, isSensitive);

        types.Add(documentType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return documentType;
    }

    public async Task<DocumentType> UpdateRulesAsync(
        Guid id,
        IReadOnlyCollection<string> allowedExtensions,
        int maxSizeMb,
        bool isEmployeeUploadable,
        bool isSensitive,
        CancellationToken cancellationToken = default)
    {
        DocumentType documentType = await types.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(DocumentType), id);

        documentType.UpdateRules(allowedExtensions, maxSizeMb, isEmployeeUploadable, isSensitive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return documentType;
    }
}
