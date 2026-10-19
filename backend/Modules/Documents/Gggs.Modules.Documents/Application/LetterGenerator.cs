using Gggs.Modules.Documents.Contracts;

namespace Gggs.Modules.Documents.Application;

/// <summary>Letters for other modules, generated the same way as from the documents screen.</summary>
internal sealed class LetterGenerator(DocumentService documents) : ILetterGenerator
{
    public Task<Guid> GenerateAsync(Guid employeeId, string templateCode, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
        => documents.GenerateByCodeAsync(employeeId, templateCode, values, cancellationToken);
}
