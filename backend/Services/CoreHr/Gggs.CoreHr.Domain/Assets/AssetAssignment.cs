using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Assets;

/// <summary>
/// One employee's custody of one <see cref="Asset"/> - open while <see cref="ReturnedDate"/> is
/// null, closed once it is returned. An asset can have at most one open assignment at a time
/// (enforced by <see cref="AssetAssignmentService"/> via <see cref="Asset.Status"/>, not a unique
/// index here, since "open" is a null-check rather than a status value this row owns). No
/// condition-in/condition-out tracking or acknowledgement workflow in this v1 slice
/// (architecture.md's <c>asset.AssetAssignment</c>).
/// </summary>
public sealed class AssetAssignment : TenantAggregateRoot
{
    private AssetAssignment()
    {
    }

    public Guid AssetId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateTime AssignedDate { get; private set; }

    public DateTime? ReturnedDate { get; private set; }

    public static AssetAssignment Create(Guid assetId, Guid employeeId, DateTime assignedAtUtc) => new()
    {
        AssetId = Guard.NotEmpty(assetId),
        EmployeeId = Guard.NotEmpty(employeeId),
        AssignedDate = assignedAtUtc,
        IsActive = true,
    };

    public void Return(DateTime returnedAtUtc)
    {
        Guard.Against(ReturnedDate is not null, "ASSET_ASSIGNMENT_ALREADY_RETURNED", "This assignment has already been returned.");
        ReturnedDate = returnedAtUtc;
    }
}
