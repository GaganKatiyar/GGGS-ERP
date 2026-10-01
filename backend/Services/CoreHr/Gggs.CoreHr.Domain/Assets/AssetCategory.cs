using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Assets;

/// <summary>
/// A named kind of physical asset - "Laptop," "Mobile Phone," "Monitor." One flat tenant-wide
/// catalog, matching every other v1 module's own simplification (no depreciation method or useful
/// life yet - architecture.md's <c>asset.AssetCategory</c>).
/// </summary>
public sealed class AssetCategory : TenantAggregateRoot
{
    private AssetCategory()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public static AssetCategory Create(string code, string name) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        IsActive = true,
    };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
