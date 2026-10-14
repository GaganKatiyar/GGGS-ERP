using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Documents.Contracts;

/// <summary>Counts other modules show next to a person, such as Employee 360's documents tab.</summary>
public interface IDocumentDirectory
{
    /// <summary>Active documents held for each employee.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountForEmployeesAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>A document was signed. A module that sent it for signature (onboarding, separation) reacts to this.</summary>
[MessageType("gggs.documents.signed")]
public sealed record DocumentSignedIntegrationEvent(Guid DocumentId, int VersionNumber, Guid SignerEmployeeId, DateTime SignedDate) : IntegrationEvent;
