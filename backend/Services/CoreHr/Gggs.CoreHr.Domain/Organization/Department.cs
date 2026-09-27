using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// A node in the department tree.
/// <para>
/// The architecture calls for a <c>HIERARCHYID</c> column so "this department and everything under
/// it" is a single indexed range scan. This uses a plain adjacency list
/// (<see cref="ParentDepartmentId"/>) instead, for a reason worth being honest about: maintaining a
/// materialized path or a <c>HIERARCHYID</c> means rewriting every descendant when a subtree moves,
/// which is state that spans aggregates and does not belong inside this one. A descendant query is
/// therefore a recursive CTE seeded from this table at the point that needs it (the data-scope
/// filter for "my department and below," when that lands), not a column this aggregate maintains.
/// Moving to <c>HIERARCHYID</c> later changes that query, not this class.
/// </para>
/// </summary>
public sealed class Department : TenantAggregateRoot
{
    private Department()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public Guid? ParentDepartmentId { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public static Department Create(string code, string name, Guid? parentDepartmentId = null) => new()
    {
        Code = OrganizationCode.Normalize(code),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        ParentDepartmentId = parentDepartmentId,
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void SetHead(Guid? employeeId) => HeadEmployeeId = employeeId;

    public void SetCostCenter(Guid? costCenterId) => CostCenterId = costCenterId;

    public void SetBusinessUnit(Guid? businessUnitId) => BusinessUnitId = businessUnitId;

    /// <summary>
    /// Moves this department under another, or to the top level.
    /// <para>
    /// Refuses only the one cycle this aggregate can see on its own - becoming its own parent. A
    /// deeper cycle (moving a department under its own grandchild) needs the whole tree to detect,
    /// so the caller walks the ancestors of <paramref name="parentId"/> before calling this, the same
    /// way <c>DepartmentManagementService</c> will for a real reparenting.
    /// </para>
    /// </summary>
    public void MoveUnder(Guid? parentId)
    {
        Guard.Against(parentId == Id, "DEPARTMENT_CANNOT_BE_OWN_PARENT", "A department cannot be its own parent.");
        ParentDepartmentId = parentId;
    }
}
