using Gggs.Data;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Infrastructure;

/// <summary>The organization structure other modules read through <see cref="IOrgStructure"/>.</summary>
internal sealed class OrgStructure(AppDbContext dbContext) : IOrgStructure
{
    public async Task<IReadOnlyList<OrgReference>> FindInvalidAsync(
        IEnumerable<OrgReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);

        var invalid = new List<OrgReference>();

        foreach (IGrouping<OrgUnitKind, OrgReference> group in references.Distinct().GroupBy(reference => reference.Kind))
        {
            List<Guid> ids = [.. group.Select(reference => reference.Id)];
            HashSet<Guid> found = [.. await ActiveIdsAsync(group.Key, ids, cancellationToken)];
            invalid.AddRange(group.Where(reference => !found.Contains(reference.Id)));
        }

        return invalid;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        OrgUnitKind kind,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        List<Guid> wanted = [.. ids.Distinct()];

        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Names of inactive records too: an employee's history still says where they sat.
        return await UnitsOf(kind)
            .AsNoTracking()
            .Where(unit => wanted.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Name, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveCodesAsync(
        OrgUnitKind kind,
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);
        List<string> wanted = [.. codes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(OrgUnit.NormalizeCode).Distinct()];

        if (wanted.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, Guid> found = await UnitsOf(kind)
            .AsNoTracking()
            .Where(unit => wanted.Contains(unit.Code) && unit.IsActive)
            .ToDictionaryAsync(unit => unit.Code, unit => unit.Id, cancellationToken);

        return new Dictionary<string, Guid>(found, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Guid?> CostCenterForAsync(Guid? locationId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        if (locationId is { } location)
        {
            var office = await dbContext.Locations
                .AsNoTracking()
                .Where(unit => unit.Id == location)
                .Select(unit => new { unit.CostCenterId, unit.CompanyId })
                .FirstOrDefaultAsync(cancellationToken);

            if (office?.CostCenterId is { } own)
            {
                return own;
            }

            companyId ??= office?.CompanyId;
        }

        List<CostCenter> defaults = await dbContext.CostCenters
            .AsNoTracking()
            .Where(unit => unit.IsDefault && unit.IsActive)
            .ToListAsync(cancellationToken);

        return companyId is { } company
            ? defaults.FirstOrDefault(unit => unit.CompanyId == company)?.Id ?? (defaults.Count == 1 ? defaults[0].Id : null)
            : defaults.Count == 1 ? defaults[0].Id : null;
    }

    public async Task<IReadOnlyList<CostCenterInfo>> CostCentersAsync(CancellationToken cancellationToken = default)
    {
        List<CostCenter> centers = await dbContext.CostCenters.AsNoTracking().OrderBy(unit => unit.Name).ToListAsync(cancellationToken);
        Dictionary<Guid, Company> companies = await dbContext.Companies.AsNoTracking().ToDictionaryAsync(unit => unit.Id, cancellationToken);

        return [.. centers.Select(center =>
        {
            Company? company = center.CompanyId is { } id ? companies.GetValueOrDefault(id) : null;
            string? country = string.IsNullOrWhiteSpace(center.Address?.CountryCode) ? company?.CountryCode : center.Address.CountryCode;
            return new CostCenterInfo(center.Id, center.Code, center.Name, center.IsActive, company?.Name, country);
        })];
    }

    public async Task<RolePlacement> PlaceRoleAsync(Guid? departmentId, Guid? roleId, Guid? levelId, Guid? designationId, CancellationToken cancellationToken = default)
    {
        if (designationId is { } designationKey)
        {
            var designation = await dbContext.Designations
                .AsNoTracking()
                .Where(unit => unit.Id == designationKey)
                .Select(unit => new { unit.Name, unit.RoleId, unit.LevelId })
                .FirstOrDefaultAsync(cancellationToken);

            if (designation?.RoleId is { } designationRole)
            {
                if (roleId is not null && roleId != designationRole)
                {
                    return new(roleId, levelId, "DESIGNATION_ROLE_MISMATCH", $"{designation.Name} is a designation of another role. Pick the matching role, or leave the role empty.", "designation");
                }

                roleId = designationRole;
            }

            if (designation?.RoleId is not null && designation.LevelId is { } designationLevel)
            {
                if (levelId is not null && levelId != designationLevel)
                {
                    return new(roleId, levelId, "DESIGNATION_ROLE_MISMATCH", $"{designation.Name} is for another level. Pick the matching level, or leave the level empty.", "designation");
                }

                levelId = designationLevel;
            }
        }

        if (roleId is { } roleKey && departmentId is { } departmentKey)
        {
            var role = await dbContext.Roles
                .AsNoTracking()
                .Where(unit => unit.Id == roleKey)
                .Select(unit => new { unit.Name, unit.DepartmentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (role?.DepartmentId is { } roleDepartment && roleDepartment != departmentKey)
            {
                var department = await dbContext.Departments
                    .AsNoTracking()
                    .Where(unit => unit.Id == departmentKey)
                    .Select(unit => new { unit.Name, unit.ParentDepartmentId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (department?.ParentDepartmentId != roleDepartment)
                {
                    string owner = await dbContext.Departments.AsNoTracking().Where(unit => unit.Id == roleDepartment).Select(unit => unit.Name).FirstOrDefaultAsync(cancellationToken) ?? "another department";
                    return new(roleId, levelId, "ROLE_NOT_IN_DEPARTMENT", $"{role.Name} is a role of {owner}, not of {department?.Name ?? "the chosen department"}.", "role");
                }
            }
        }

        return new(roleId, levelId);
    }

    public async Task<Guid?> GetDepartmentHeadAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        // Walk up at most a dozen levels: deeper trees do not exist in practice, and a bound keeps a
        // data error (a cycle) from turning into an endless loop.
        Guid? current = departmentId;
        for (int depth = 0; depth < 12 && current is { } id; depth++)
        {
            var department = await dbContext.Departments
                .AsNoTracking()
                .Where(unit => unit.Id == id)
                .Select(unit => new { unit.HeadEmployeeId, unit.ParentDepartmentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (department is null)
            {
                return null;
            }

            if (department.HeadEmployeeId is { } head)
            {
                return head;
            }

            current = department.ParentDepartmentId;
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DepartmentHeadsAsync(CancellationToken cancellationToken = default)
    {
        var heads = await dbContext.Departments
            .AsNoTracking()
            .Where(unit => unit.IsActive && unit.HeadEmployeeId != null)
            .Select(unit => new { Head = unit.HeadEmployeeId!.Value, unit.Id })
            .ToListAsync(cancellationToken);

        return heads.GroupBy(row => row.Head).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)[.. group.Select(row => row.Id)]);
    }

    public async Task<IReadOnlyList<DepartmentNode>> DepartmentsAsync(CancellationToken cancellationToken = default)
        => await dbContext.Departments
            .AsNoTracking()
            .OrderBy(unit => unit.Name)
            .Select(unit => new DepartmentNode(unit.Id, unit.Code, unit.Name, unit.ParentDepartmentId, unit.IsActive))
            .ToListAsync(cancellationToken);

    private async Task<List<Guid>> ActiveIdsAsync(OrgUnitKind kind, List<Guid> ids, CancellationToken cancellationToken)
        => await UnitsOf(kind)
            .AsNoTracking()
            .Where(unit => ids.Contains(unit.Id) && unit.IsActive)
            .Select(unit => unit.Id)
            .ToListAsync(cancellationToken);

    private IQueryable<OrgUnit> UnitsOf(OrgUnitKind kind) => kind switch
    {
        OrgUnitKind.Company => dbContext.Companies,
        OrgUnitKind.BusinessUnit => dbContext.BusinessUnits,
        OrgUnitKind.Department => dbContext.Departments,
        OrgUnitKind.Location => dbContext.Locations,
        OrgUnitKind.CostCenter => dbContext.CostCenters,
        OrgUnitKind.Level => dbContext.Levels,
        OrgUnitKind.Role => dbContext.Roles,
        OrgUnitKind.Designation => dbContext.Designations,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown organization unit kind."),
    };
}
