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

    public async Task<PositionPlacement?> GetPositionAsync(Guid positionId, CancellationToken cancellationToken = default)
        => await dbContext.Positions
            .AsNoTracking()
            .Where(position => position.Id == positionId && position.IsActive)
            .Select(position => new PositionPlacement(
                position.Id, position.JobId, position.DepartmentId, position.LocationId, position.GradeId, position.ReportsToPositionId, position.Headcount))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<List<Guid>> ActiveIdsAsync(OrgUnitKind kind, List<Guid> ids, CancellationToken cancellationToken)
        => await UnitsOf(kind)
            .AsNoTracking()
            .Where(unit => ids.Contains(unit.Id) && unit.IsActive)
            .Select(unit => unit.Id)
            .ToListAsync(cancellationToken);

    private IQueryable<OrgUnit> UnitsOf(OrgUnitKind kind) => kind switch
    {
        OrgUnitKind.LegalEntity => dbContext.LegalEntities,
        OrgUnitKind.BusinessUnit => dbContext.BusinessUnits,
        OrgUnitKind.Division => dbContext.Divisions,
        OrgUnitKind.Department => dbContext.Departments,
        OrgUnitKind.Location => dbContext.Locations,
        OrgUnitKind.CostCenter => dbContext.CostCenters,
        OrgUnitKind.ProfitCenter => dbContext.ProfitCenters,
        OrgUnitKind.Grade => dbContext.Grades,
        OrgUnitKind.JobLevel => dbContext.JobLevels,
        OrgUnitKind.Job => dbContext.Jobs,
        OrgUnitKind.Position => dbContext.Positions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown organization unit kind."),
    };
}
