using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Assets;

namespace Gggs.CoreHr.Application.Assets;

public sealed class AssetService(IAssetRepository assets, IAssetCategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<Asset> CreateAsync(
        string assetTag, Guid categoryId, string make, string model, string? serialNumber, CancellationToken cancellationToken = default)
    {
        _ = await categories.GetAsync(categoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(AssetCategory), categoryId);

        Guard(
            !await assets.AssetTagExistsAsync(assetTag, cancellationToken),
            "ASSET_TAG_ALREADY_EXISTS",
            $"An asset with tag '{assetTag}' already exists.");

        Asset asset = Asset.Create(assetTag, categoryId, make, model, serialNumber);
        assets.Add(asset);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<Asset> SendForRepairAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Asset asset = await GetOrThrowAsync(id, cancellationToken);
        asset.SendForRepair();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<Asset> ReturnFromRepairAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Asset asset = await GetOrThrowAsync(id, cancellationToken);
        asset.ReturnFromRepair();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<Asset> RetireAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Asset asset = await GetOrThrowAsync(id, cancellationToken);
        asset.Retire();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    private async Task<Asset> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await assets.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Asset), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
