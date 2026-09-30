using Gggs.CoreHr.Domain.Organization;

namespace Gggs.CoreHr.Application.Organization;

/// <summary>
/// Persistence for every organization structure entity, kept as one port rather than eight.
/// <para>
/// Each of these aggregates is a handful of fields and almost no cross-aggregate behaviour beyond
/// "does this code already exist" and "does the parent I am pointing at exist" - splitting them into
/// eight repository interfaces would multiply the ceremony without buying any real independence,
/// since the same <c>CoreHrDbContext</c> answers all of them.
/// </para>
/// </summary>
public interface IOrganizationRepository
{
    Task<LegalEntity?> GetLegalEntityAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LegalEntity?> GetLegalEntityByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> LegalEntityCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(LegalEntity entity);

    Task<BusinessUnit?> GetBusinessUnitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> BusinessUnitCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(BusinessUnit entity);

    Task<Branch?> GetBranchAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Branch?> GetBranchByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> BranchCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Branch entity);

    Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> LocationCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Location entity);

    Task<Department?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Department?> GetDepartmentByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> DepartmentCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Department entity);

    Task<Designation?> GetDesignationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Designation?> GetDesignationByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> DesignationCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Designation entity);

    Task<Grade?> GetGradeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Grade?> GetGradeByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<bool> GradeCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Grade entity);

    Task<CostCenter?> GetCostCenterAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CostCenterCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(CostCenter entity);
}
