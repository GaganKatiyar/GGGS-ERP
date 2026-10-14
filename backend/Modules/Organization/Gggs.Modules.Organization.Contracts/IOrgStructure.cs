namespace Gggs.Modules.Organization.Contracts;

/// <summary>The kinds of organization record another module may point at.</summary>
public enum OrgUnitKind
{
    LegalEntity = 1,
    BusinessUnit = 2,
    Division = 3,
    Department = 4,
    Location = 5,
    CostCenter = 6,
    ProfitCenter = 7,
    Grade = 8,
    JobLevel = 9,
    Job = 10,
    Position = 11,
}

/// <summary>One reference to check: "this id should be an active record of this kind".</summary>
public sealed record OrgReference(OrgUnitKind Kind, Guid Id);

/// <summary>What a position says about the seat an employee is placed in.</summary>
public sealed record PositionPlacement(Guid PositionId, Guid JobId, Guid DepartmentId, Guid? LocationId, Guid? GradeId, Guid? ReportsToPositionId, int Headcount);

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

    /// <summary>The job, department and reporting line a position defines, or null when it does not exist.</summary>
    Task<PositionPlacement?> GetPositionAsync(Guid positionId, CancellationToken cancellationToken = default);
}
