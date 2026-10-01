using Gggs.CoreHr.Domain.Assets;

namespace Gggs.CoreHr.Application.Assets;

public interface IAssetCategoryRepository
{
    Task<AssetCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(AssetCategory category);
}

public interface IAssetRepository
{
    Task<Asset?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> AssetTagExistsAsync(string assetTag, CancellationToken cancellationToken = default);

    void Add(Asset asset);
}

public interface IAssetAssignmentRepository
{
    Task<AssetAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(AssetAssignment assignment);
}
