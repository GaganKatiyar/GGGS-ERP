using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>Where cost rolls up to for reporting - independent of the department tree, because the two do not always match.</summary>
public sealed class CostCenter : TenantAggregateRoot
{
    private CostCenter()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? GlAccountCode { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? ParentCostCenterId { get; private set; }

    public static CostCenter Create(string code, string name, string? glAccountCode = null) => new()
    {
        Code = OrganizationCode.Normalize(code),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        GlAccountCode = string.IsNullOrWhiteSpace(glAccountCode) ? null : Guard.MaxLength(glAccountCode.Trim(), 50),
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void SetOwner(Guid? employeeId) => OwnerEmployeeId = employeeId;

    /// <summary>
    /// Moves this cost center under another, or to the top level when <paramref name="parentId"/>
    /// is null. The caller checks for cycles across the whole tree; a single node cannot see far
    /// enough to catch one on its own.
    /// </summary>
    public void MoveUnder(Guid? parentId)
    {
        Guard.Against(parentId == Id, "COST_CENTER_CANNOT_BE_OWN_PARENT", "A cost center cannot be its own parent.");
        ParentCostCenterId = parentId;
    }
}
