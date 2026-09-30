using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Infrastructure;

/// <summary>Organization structure persistence, scoped to the current tenant by the query filter.</summary>
public sealed class OrganizationRepository(CoreHrDbContext dbContext) : IOrganizationRepository
{
    public async Task<LegalEntity?> GetLegalEntityAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<LegalEntity?> GetLegalEntityByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.LegalEntities.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

    public async Task<bool> LegalEntityCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.LegalEntities.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(LegalEntity entity) => dbContext.LegalEntities.Add(entity);

    public async Task<BusinessUnit?> GetBusinessUnitAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.BusinessUnits.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> BusinessUnitCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.BusinessUnits.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(BusinessUnit entity) => dbContext.BusinessUnits.Add(entity);

    public async Task<Branch?> GetBranchAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Branches.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Branch?> GetBranchByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Branches.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

    public async Task<bool> BranchCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Branches.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Branch entity) => dbContext.Branches.Add(entity);

    public async Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Locations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> LocationCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Locations.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Location entity) => dbContext.Locations.Add(entity);

    public async Task<Department?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Departments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Department?> GetDepartmentByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Departments.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

    public async Task<bool> DepartmentCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Departments.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Department entity) => dbContext.Departments.Add(entity);

    public async Task<Designation?> GetDesignationAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Designations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Designation?> GetDesignationByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Designations.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

    public async Task<bool> DesignationCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Designations.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Designation entity) => dbContext.Designations.Add(entity);

    public async Task<Grade?> GetGradeAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Grades.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Grade?> GetGradeByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Grades.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

    public async Task<bool> GradeCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Grades.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Grade entity) => dbContext.Grades.Add(entity);

    public async Task<CostCenter?> GetCostCenterAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.CostCenters.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CostCenterCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.CostCenters.AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(CostCenter entity) => dbContext.CostCenters.Add(entity);
}
