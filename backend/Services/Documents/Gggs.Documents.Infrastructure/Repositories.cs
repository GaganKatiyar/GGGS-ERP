using Gggs.Documents.Application.Documents;
using Gggs.Documents.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Documents.Infrastructure;

public sealed class DocumentTypeRepository(DocumentDbContext dbContext) : IDocumentTypeRepository
{
    public async Task<DocumentType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await dbContext.DocumentTypes.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public async Task<DocumentType?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.DocumentTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
        => await dbContext.DocumentTypes.AnyAsync(x => x.Code == code, cancellationToken);

    public void Add(DocumentType documentType) => dbContext.DocumentTypes.Add(documentType);
}

public sealed class DocumentRepository(DocumentDbContext dbContext) : IDocumentRepository
{
    public async Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Documents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Document document) => dbContext.Documents.Add(document);

    public void Remove(Document document) => dbContext.Documents.Remove(document);
}
