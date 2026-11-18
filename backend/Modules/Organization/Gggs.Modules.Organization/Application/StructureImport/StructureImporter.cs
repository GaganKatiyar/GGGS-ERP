using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Organization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Application.StructureImport;

/// <summary>What an applied import did.</summary>
public sealed record StructureImportResult(
    int DepartmentsCreated, int DepartmentsUpdated,
    int FunctionsCreated, int FunctionsUpdated,
    int RolesCreated, int RolesUpdated,
    int DesignationsCreated);

/// <summary>Reads the app's current structure for the planner, and applies a plan to it. Public for the dev seed.</summary>
public static class StructureImporter
{
    public static async Task<StructureSnapshot> SnapshotAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        List<ExistingUnit> departments = await db.Departments.AsNoTracking()
            .Select(unit => new ExistingUnit(unit.Id, unit.Code, unit.Name, unit.ParentDepartmentId, unit.IsActive))
            .ToListAsync(cancellationToken);
        List<ExistingUnit> roles = await db.Roles.AsNoTracking()
            .Select(unit => new ExistingUnit(unit.Id, unit.Code, unit.Name, unit.DepartmentId, unit.IsActive))
            .ToListAsync(cancellationToken);
        List<ExistingUnit> levels = await db.Levels.AsNoTracking()
            .Where(unit => unit.IsActive)
            .OrderBy(unit => unit.Rank)
            .Select(unit => new ExistingUnit(unit.Id, unit.Code, unit.Name, null, unit.IsActive))
            .ToListAsync(cancellationToken);
        List<string> designationCodes = await db.Designations.AsNoTracking().Select(unit => unit.Code).ToListAsync(cancellationToken);

        return new StructureSnapshot(departments, roles, levels, designationCodes);
    }

    /// <summary>Applies <paramref name="plan"/> to the tracked context. The caller saves, so it all lands in one transaction.</summary>
    public static async Task<StructureImportResult> ApplyAsync(AppDbContext db, StructurePlan plan, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply)
        {
            throw new DomainRuleException("IMPORT_HAS_ERRORS", $"The file has {plan.Errors.Count} problem(s). Fix them and preview again.");
        }

        Dictionary<string, Department> departments = (await db.Departments.ToListAsync(cancellationToken))
            .ToDictionary(unit => unit.Code, StringComparer.OrdinalIgnoreCase);

        foreach (PlannedDepartment planned in plan.Departments)
        {
            if (planned.Change == PlannedChange.Create)
            {
                Department department = Department.Create(planned.Code, planned.Name, today);
                db.Departments.Add(department);
                departments[planned.Code] = department;
            }
            else if (planned.Change == PlannedChange.Update)
            {
                Department department = departments[planned.Code];
                department.Rename(planned.Name);
                department.Activate();
            }
        }

        foreach (PlannedFunction planned in plan.Functions.Where(unit => unit.Change != PlannedChange.Unchanged))
        {
            Department parent = departments[planned.DepartmentCode];
            if (planned.Change == PlannedChange.Create)
            {
                Department function = Department.Create(planned.Code, planned.Name, today);
                function.Place(parent.Id, parent.BusinessUnitId, null, null, null);
                db.Departments.Add(function);
                departments[planned.Code] = function;
            }
            else
            {
                Department function = departments[planned.Code];
                function.Rename(planned.Name);
                function.Place(parent.Id, parent.BusinessUnitId, function.HeadEmployeeId, function.CostCenterId, function.LocationId);
                function.Activate();
            }
        }

        Dictionary<string, Role> roles = (await db.Roles.ToListAsync(cancellationToken))
            .ToDictionary(unit => unit.Code, StringComparer.OrdinalIgnoreCase);
        var touched = new List<Role>();

        foreach (PlannedRole planned in plan.Roles)
        {
            Department department = departments[planned.DepartmentCode];
            if (planned.Change == PlannedChange.Create)
            {
                Role role = Role.Create(planned.Code, planned.Name);
                role.Define(null, null, department.Id, null, null, null, null);
                db.Roles.Add(role);
                touched.Add(role);
            }
            else
            {
                Role role = roles[planned.Code];
                if (planned.Change == PlannedChange.Update)
                {
                    role.Rename(planned.Name);
                    role.Define(role.JobFamilyCode, role.LevelId, department.Id, role.MinimumExperienceYears, role.MaximumExperienceYears, role.Description, [.. role.RequiredSkills]);
                    role.Activate();
                }

                touched.Add(role);
            }
        }

        int designations = await RoleDesignations.SyncAsync(db, touched, cancellationToken);

        return new StructureImportResult(
            plan.Departments.Count(unit => unit.Change == PlannedChange.Create),
            plan.Departments.Count(unit => unit.Change == PlannedChange.Update),
            plan.Functions.Count(unit => unit.Change == PlannedChange.Create),
            plan.Functions.Count(unit => unit.Change == PlannedChange.Update),
            plan.Roles.Count(unit => unit.Change == PlannedChange.Create),
            plan.Roles.Count(unit => unit.Change == PlannedChange.Update),
            designations);
    }
}
