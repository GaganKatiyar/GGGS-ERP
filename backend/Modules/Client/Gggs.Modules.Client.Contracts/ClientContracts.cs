namespace Gggs.Modules.Client.Contracts;

/// <summary>What other modules need to know about a client, without reading its tables.</summary>
public sealed record ClientSummary(Guid Id, string Code, string Name, bool CanTakeWork, Guid? AccountManagerEmployeeId);

/// <summary>Client lookups for contracts and projects (ADR-12).</summary>
public interface IClientDirectory
{
    Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, ClientSummary>> GetManyAsync(IEnumerable<Guid> clientIds, CancellationToken cancellationToken = default);
}
