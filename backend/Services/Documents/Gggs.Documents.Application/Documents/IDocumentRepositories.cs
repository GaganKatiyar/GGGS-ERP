using Gggs.Documents.Domain.Documents;

namespace Gggs.Documents.Application.Documents;

public interface IDocumentTypeRepository
{
    Task<DocumentType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<DocumentType?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(DocumentType documentType);
}

public interface IDocumentRepository
{
    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Document document);

    void Remove(Document document);
}
