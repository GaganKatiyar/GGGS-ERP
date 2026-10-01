using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Assets;

public enum AssetStatus
{
    InStock = 1,
    Assigned = 2,
    InRepair = 3,
    Retired = 4,
}

/// <summary>
/// One physical, tracked item - a laptop, a phone, a monitor. <see cref="Status"/> is derived from
/// its <see cref="AssetAssignment"/> history rather than set directly except for
/// <see cref="SendForRepair"/>/<see cref="Retire"/>: <see cref="AssetAssignmentService"/> flips
/// InStock -&gt; Assigned on assignment and Assigned -&gt; InStock on return, so this entity's own
/// surface is deliberately small. No warranty, vendor, condition tracking, maintenance history or
/// software license link in this v1 slice (architecture.md's <c>asset.Asset</c>) - just enough to
/// register an asset and know who has it.
/// </summary>
public sealed class Asset : TenantAggregateRoot
{
    private Asset()
    {
        AssetTag = null!;
        Make = null!;
        Model = null!;
    }

    public string AssetTag { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Make { get; private set; }

    public string Model { get; private set; }

    public string? SerialNumber { get; private set; }

    public AssetStatus Status { get; private set; }

    public static Asset Create(string assetTag, Guid categoryId, string make, string model, string? serialNumber) => new()
    {
        AssetTag = Guard.MaxLength(Guard.NotNullOrWhiteSpace(assetTag).Trim().ToUpperInvariant(), 30),
        CategoryId = Guard.NotEmpty(categoryId),
        Make = Guard.MaxLength(Guard.NotNullOrWhiteSpace(make).Trim(), 100),
        Model = Guard.MaxLength(Guard.NotNullOrWhiteSpace(model).Trim(), 100),
        SerialNumber = serialNumber is null ? null : Guard.MaxLength(serialNumber.Trim(), 100),
        Status = AssetStatus.InStock,
        IsActive = true,
    };

    /// <summary>InStock -&gt; Assigned, called by <see cref="AssetAssignmentService"/> when it
    /// creates the <see cref="AssetAssignment"/> row, not directly - the two always change
    /// together.</summary>
    public void MarkAssigned()
    {
        Guard.Against(Status != AssetStatus.InStock, "ASSET_NOT_IN_STOCK", $"Asset '{AssetTag}' must be InStock to assign; it is {Status}.");
        Status = AssetStatus.Assigned;
    }

    /// <summary>Assigned -&gt; InStock, called by <see cref="AssetAssignmentService"/> when the
    /// matching <see cref="AssetAssignment"/> is returned.</summary>
    public void MarkReturned()
    {
        Guard.Against(Status != AssetStatus.Assigned, "ASSET_NOT_ASSIGNED", $"Asset '{AssetTag}' must be Assigned to return; it is {Status}.");
        Status = AssetStatus.InStock;
    }

    public void SendForRepair()
    {
        Guard.Against(Status != AssetStatus.InStock, "ASSET_NOT_IN_STOCK", $"Asset '{AssetTag}' must be InStock to send for repair; it is {Status}.");
        Status = AssetStatus.InRepair;
    }

    public void ReturnFromRepair()
    {
        Guard.Against(Status != AssetStatus.InRepair, "ASSET_NOT_IN_REPAIR", $"Asset '{AssetTag}' must be InRepair to return it to stock; it is {Status}.");
        Status = AssetStatus.InStock;
    }

    public void Retire()
    {
        Guard.Against(Status == AssetStatus.Assigned, "ASSET_STILL_ASSIGNED", $"Asset '{AssetTag}' must be returned before it can be retired.");
        Guard.Against(Status == AssetStatus.Retired, "ASSET_ALREADY_RETIRED", $"Asset '{AssetTag}' is already retired.");
        Status = AssetStatus.Retired;
    }
}
