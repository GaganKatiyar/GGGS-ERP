using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Documents.Contracts;

/// <summary>Counts other modules show next to a person, such as Employee 360's documents tab.</summary>
public interface IDocumentDirectory
{
    /// <summary>Active documents held for each employee.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountForEmployeesAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who may read and add documents held for a record another module owns - a project, a client.
/// The owning module registers one per kind of record; Documents asks it rather than guessing,
/// so project documents follow project access and client documents follow client access.
/// </summary>
public interface IDocumentOwnerAccess
{
    /// <summary>The kind of owner this answers for: <c>Project</c> or <c>Client</c>.</summary>
    string OwnerType { get; }

    Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default);
}

/// <summary>A document was signed. A module that sent it for signature (onboarding, separation) reacts to this.</summary>
[MessageType("gggs.documents.signed")]
public sealed record DocumentSignedIntegrationEvent(Guid DocumentId, int VersionNumber, Guid SignerEmployeeId, DateTime SignedDate) : IntegrationEvent;
