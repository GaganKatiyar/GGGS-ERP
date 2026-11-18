using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Organization.Contracts;

/// <summary>The kinds of organization record another module may point at.</summary>
public enum OrgUnitKind
{
    Company = 1,
    BusinessUnit = 2,

    // 3 was Division, removed: a department sits directly in a business unit.
    Department = 4,
    Location = 5,
    CostCenter = 6,

    // 7 was ProfitCenter and 8 was Grade, removed: a level does a grade's job.
    Level = 9,
    Role = 10,

    // 11 was Position, removed: people are placed in a department with a role.
    Designation = 12,
}

/// <summary>One reference to check: "this id should be an active record of this kind".</summary>
public sealed record OrgReference(OrgUnitKind Kind, Guid Id);

/// <summary>A cost center as other modules list it: its company and the country it is in (its own address's, else its company's).</summary>
public sealed record CostCenterInfo(Guid Id, string Code, string Name, bool IsActive, string? CompanyName, string? CountryCode);

/// <summary>A department and where it sits in the tree.</summary>
public sealed record DepartmentNode(Guid Id, string Code, string Name, Guid? ParentDepartmentId, bool IsActive);

/// <summary>
/// Someone's role, level and designation once checked against their department. A designation that
/// is a role at a level fills in an empty role or level. <see cref="ErrorCode"/> is set when they do
/// not fit: <c>ROLE_NOT_IN_DEPARTMENT</c> or <c>DESIGNATION_ROLE_MISMATCH</c>.
/// </summary>
public sealed record RolePlacement(Guid? RoleId, Guid? LevelId, string? ErrorCode = null, string? ErrorMessage = null, string? Field = null)
{
    public bool Fits => ErrorCode is null;
}

/// <summary>
/// The organization structure as other modules see it (ADR-12): reference checks and display names,
/// never the tables themselves.
/// </summary>
public interface IOrgStructure
{
    /// <summary>The references that do not resolve to an active record in the current tenant. Empty when all are good.</summary>
    Task<IReadOnlyList<OrgReference>> FindInvalidAsync(IEnumerable<OrgReference> references, CancellationToken cancellationToken = default);

    /// <summary>Display names ("Engineering", "Bengaluru HQ") for ids of one kind; unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(OrgUnitKind kind, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Ids for codes of one kind (active records only), for imports that name records by code. Unknown codes are left out.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ResolveCodesAsync(OrgUnitKind kind, IEnumerable<string> codes, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cost center someone at <paramref name="locationId"/> works under: the office's own when it
    /// has one, otherwise the default of <paramref name="companyId"/> (or of the only company).
    /// Null when there is none to give.
    /// </summary>
    Task<Guid?> CostCenterForAsync(Guid? locationId, Guid? companyId, CancellationToken cancellationToken = default);

    /// <summary>Every cost center, closed ones included, with its company and country.</summary>
    Task<IReadOnlyList<CostCenterInfo>> CostCentersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a role, level and designation against a department. A role belongs to a department
    /// and is open to all of its functions; a role with no department fits anywhere.
    /// </summary>
    Task<RolePlacement> PlaceRoleAsync(Guid? departmentId, Guid? roleId, Guid? levelId, Guid? designationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The head of a department, or - when it has none - of the nearest parent department that has
    /// one. Null when no department up the chain names a head. Approval routing uses this.
    /// </summary>
    Task<Guid?> GetDepartmentHeadAsync(Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>Who heads which open departments: each head's employee id and the departments they head.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DepartmentHeadsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every department, closed ones included so a tree can still be walked through them.</summary>
    Task<IReadOnlyList<DepartmentNode>> DepartmentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A department gained, lost or changed its head, or closed with one. Identity reacts by moving the
/// Department Head role when that role is assigned automatically.
/// </summary>
[MessageType("gggs.organization.department-head-changed")]
public sealed record DepartmentHeadChangedIntegrationEvent(Guid DepartmentId, Guid? PreviousHeadEmployeeId, Guid? HeadEmployeeId) : IntegrationEvent;
