namespace Gggs.Modules.Contract.Contracts;

/// <summary>What a project needs to know about the contract it delivers against.</summary>
public sealed record ContractSummary(
    Guid Id,
    string Number,
    string Title,
    Guid ClientId,
    string Type,
    string Status,
    string BillingModel,
    string Currency,
    decimal? Value,
    DateOnly StartDate,
    DateOnly? EndDate)
{
    /// <summary>Terminated and expired contracts take no new projects.</summary>
    public bool IsLive => Status is "Draft" or "PendingApproval" or "Active";
}

/// <summary>Contract lookups for projects (ADR-12).</summary>
public interface IContractDirectory
{
    Task<IReadOnlyDictionary<Guid, ContractSummary>> GetManyAsync(IEnumerable<Guid> contractIds, CancellationToken cancellationToken = default);
}
