using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Assets;

namespace Gggs.CoreHr.Application.Assets;

public sealed class AssetCategoryService(IAssetCategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<AssetCategory> CreateAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        Guard(
            !await categories.CodeExistsAsync(code, cancellationToken),
            "ASSET_CATEGORY_CODE_ALREADY_EXISTS",
            $"A category with code '{code}' already exists.");

        AssetCategory category = AssetCategory.Create(code, name);
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<AssetCategory> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssetCategory category = await GetOrThrowAsync(id, cancellationToken);
        category.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<AssetCategory> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssetCategory category = await GetOrThrowAsync(id, cancellationToken);
        category.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    private async Task<AssetCategory> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await categories.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AssetCategory), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
