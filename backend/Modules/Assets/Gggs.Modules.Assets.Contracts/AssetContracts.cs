namespace Gggs.Modules.Assets.Contracts;

/// <summary>An asset someone holds, and what it is worth today.</summary>
public sealed record HeldAsset(Guid AssetId, string Tag, string Name, string Category, DateOnly AssignedOn, decimal BookValue);

/// <summary>Who holds which company assets, for exit clearance (ADR-12).</summary>
public interface IAssetLedger
{
    Task<IReadOnlyList<HeldAsset>> HeldByAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
