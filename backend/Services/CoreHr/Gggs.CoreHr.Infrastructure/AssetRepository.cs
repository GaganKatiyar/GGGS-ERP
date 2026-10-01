using Gggs.CoreHr.Application.Assets;
using Gggs.CoreHr.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Infrastructure;

public sealed class AssetCategoryRepository(CoreHrDbContext dbContext) : IAssetCategoryRepository
{
    public async Task<AssetCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.AssetCategories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.AssetCategories.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(AssetCategory category) => dbContext.AssetCategories.Add(category);
}

public sealed class AssetRepository(CoreHrDbContext dbContext) : IAssetRepository
{
    public async Task<Asset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Assets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> AssetTagExistsAsync(string assetTag, CancellationToken cancellationToken = default)
    {
        string normalized = assetTag.Trim().ToUpperInvariant();
        return await dbContext.Assets.AnyAsync(x => x.AssetTag == normalized, cancellationToken);
    }

    public void Add(Asset asset) => dbContext.Assets.Add(asset);
}

public sealed class AssetAssignmentRepository(CoreHrDbContext dbContext) : IAssetAssignmentRepository
{
    public async Task<AssetAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.AssetAssignments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AssetAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.AssetAssignments
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.AssignedDate)
            .ToListAsync(cancellationToken);

    public void Add(AssetAssignment assignment) => dbContext.AssetAssignments.Add(assignment);
}
