using Gggs.Modules.Organization.Domain;

namespace Gggs.Modules.Organization.Application.StructureImport;

public enum PlannedChange
{
    Create,
    Update,
    Unchanged,
}

/// <summary>A record already in the app, as the planner sees it. <see cref="ParentId"/> is a function's department or a role's department.</summary>
public sealed record ExistingUnit(Guid Id, string Code, string Name, Guid? ParentId, bool IsActive);

/// <summary>What the app already holds, for matching the file against.</summary>
public sealed record StructureSnapshot(
    IReadOnlyList<ExistingUnit> Departments,
    IReadOnlyList<ExistingUnit> Roles,
    IReadOnlyList<ExistingUnit> ActiveLevels,
    IReadOnlyCollection<string> DesignationCodes);

public sealed record PlannedDepartment(string Code, string Name, PlannedChange Change, Guid? ExistingId, string? Note);

public sealed record PlannedFunction(string Code, string Name, string DepartmentCode, PlannedChange Change, Guid? ExistingId, string? Note);

public sealed record PlannedRole(string Code, string Name, string DepartmentCode, PlannedChange Change, Guid? ExistingId, string? Note);

/// <summary>What applying the file would do. Nothing is applied while <see cref="Errors"/> has entries.</summary>
public sealed record StructurePlan(
    int RowsRead,
    int DuplicateRows,
    IReadOnlyList<PlannedDepartment> Departments,
    IReadOnlyList<PlannedFunction> Functions,
    IReadOnlyList<PlannedRole> Roles,
    int NewDesignations,
    IReadOnlyList<StructureIssue> Errors,
    IReadOnlyList<StructureIssue> Warnings)
{
    public bool CanApply => Errors.Count == 0;
}

/// <summary>
/// Works out what an org-structure file would change, without touching anything (so it can be
/// previewed and tested). Departments are matched by code, else by name; functions, roles and
/// levels by code. A role belongs to its row's department and is open to all of its functions.
/// Only adds and updates: nothing missing from the file is deactivated.
/// </summary>
public static class StructureImportPlanner
{
    private const int MaxCode = 32;

    public static StructurePlan Plan(IReadOnlyList<StructureRow> rows, StructureSnapshot existing)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(existing);

        var errors = new List<StructureIssue>();
        var warnings = new List<StructureIssue>();

        // ----- Duplicates and department-only lines ---------------------------------------------
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<StructureRow>();
        int duplicates = 0;
        var departmentOnly = new List<StructureRow>();

        foreach (StructureRow row in rows)
        {
            string key = string.Join('\u001f', row.Department, row.DepartmentCode, row.FunctionName, row.FunctionCode, row.RoleName, row.RoleCode, row.LevelCode);
            if (!seen.Add(key))
            {
                duplicates++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Department))
            {
                errors.Add(new(row.Line, "IMPORT_DEPARTMENT_MISSING", "The department name is empty."));
                continue;
            }

            if (row.FunctionName is null && row.FunctionCode is null && row.RoleName is null && row.RoleCode is null)
            {
                departmentOnly.Add(row);
                continue;
            }

            unique.Add(row);
        }

        // ----- Departments ------------------------------------------------------------------------
        Dictionary<string, ExistingUnit> departmentsByCode = existing.Departments.ToDictionary(unit => unit.Code, StringComparer.OrdinalIgnoreCase);
        var departmentCodeByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var departmentNameByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (StructureRow row in unique)
        {
            string name = row.Department!;
            string code = row.DepartmentCode is { } given
                ? Norm(given)
                : departmentCodeByName.GetValueOrDefault(name)
                    ?? existing.Departments.FirstOrDefault(unit => unit.ParentId is null && unit.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Code
                    ?? GeneratedDepartmentCode(name);

            if (departmentCodeByName.TryGetValue(name, out string? earlier) && earlier != code)
            {
                errors.Add(new(row.Line, "IMPORT_DEPARTMENT_CODES_DIFFER", $"{name} has the code {earlier} on an earlier line and {code} here."));
                continue;
            }

            if (departmentNameByCode.TryGetValue(code, out string? other) && !other.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new(row.Line, "IMPORT_DEPARTMENT_CODE_SHARED", $"The code {code} is given to both {other} and {name}."));
                continue;
            }

            if (CodeProblem(code) is { } problem)
            {
                errors.Add(new(row.Line, "IMPORT_CODE_INVALID", $"Department code {code}: {problem}"));
                continue;
            }

            departmentCodeByName[name] = code;
            departmentNameByCode[code] = name;
        }

        var departments = new List<PlannedDepartment>();
        foreach ((string code, string name) in departmentNameByCode)
        {
            if (departmentsByCode.TryGetValue(code, out ExistingUnit? match))
            {
                if (match.ParentId is not null)
                {
                    errors.Add(new(0, "IMPORT_CODE_CLASH", $"The department code {code} is already used by a function ({match.Name}). Give {name} another code."));
                    continue;
                }

                bool renamed = match.Name != name;
                departments.Add(new(code, name, renamed || !match.IsActive ? PlannedChange.Update : PlannedChange.Unchanged, match.Id,
                    Note(renamed ? $"renamed from {match.Name}" : null, match.IsActive ? null : "reactivated")));
            }
            else
            {
                departments.Add(new(code, name, PlannedChange.Create, null, null));
            }
        }

        foreach (string name in departmentOnly.Select(row => row.Department!).Distinct(StringComparer.OrdinalIgnoreCase).Where(name => !departmentCodeByName.ContainsKey(name)))
        {
            int line = departmentOnly.First(row => row.Department!.Equals(name, StringComparison.OrdinalIgnoreCase)).Line;
            warnings.Add(new(line, "IMPORT_DEPARTMENT_EMPTY", $"{name} has no functions or roles, so it is skipped."));
        }

        // ----- Functions --------------------------------------------------------------------------
        var functions = new Dictionary<string, (string Name, string Department, int Line)>(StringComparer.OrdinalIgnoreCase);
        foreach (StructureRow row in unique.Where(row => row.FunctionName is not null || row.FunctionCode is not null))
        {
            if (!departmentCodeByName.TryGetValue(row.Department!, out string? departmentCode))
            {
                continue;
            }

            if (row.FunctionCode is null || row.FunctionName is null)
            {
                errors.Add(new(row.Line, "IMPORT_FUNCTION_INCOMPLETE", "A function needs both a name and a code."));
                continue;
            }

            string code = Norm(row.FunctionCode);
            if (CodeProblem(code) is { } problem)
            {
                errors.Add(new(row.Line, "IMPORT_CODE_INVALID", $"Function code {code}: {problem}"));
                continue;
            }

            if (departmentNameByCode.ContainsKey(code))
            {
                errors.Add(new(row.Line, "IMPORT_CODE_CLASH", $"The function code {code} is also a department's code. Departments and functions share codes, so one must change."));
                continue;
            }

            if (functions.TryGetValue(code, out var earlier))
            {
                if (!earlier.Department.Equals(departmentCode, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new(row.Line, "IMPORT_FUNCTION_TWO_DEPARTMENTS", $"The function {code} is under {departmentNameByCode[earlier.Department]} on line {earlier.Line} and under {row.Department} here."));
                }
                else if (!earlier.Name.Equals(row.FunctionName, StringComparison.Ordinal))
                {
                    errors.Add(new(row.Line, "IMPORT_NAMES_DIFFER", $"The function {code} is called {earlier.Name} on line {earlier.Line} and {row.FunctionName} here."));
                }

                continue;
            }

            functions[code] = (row.FunctionName, departmentCode, row.Line);
        }

        Dictionary<Guid, string> departmentCodeById = existing.Departments.ToDictionary(unit => unit.Id, unit => unit.Code);
        var plannedFunctions = new List<PlannedFunction>();
        foreach ((string code, (string name, string departmentCode, int line)) in functions)
        {
            if (!departmentsByCode.TryGetValue(code, out ExistingUnit? match))
            {
                plannedFunctions.Add(new(code, name, departmentCode, PlannedChange.Create, null, null));
                continue;
            }

            if (match.ParentId is null)
            {
                errors.Add(new(line, "IMPORT_CODE_CLASH", $"The function code {code} is already used by the department {match.Name}. Change that department's code first."));
                continue;
            }

            string? currentDepartment = departmentCodeById.GetValueOrDefault(match.ParentId.Value);
            bool moved = !string.Equals(currentDepartment, departmentCode, StringComparison.OrdinalIgnoreCase);
            bool renamed = match.Name != name;
            plannedFunctions.Add(new(code, name, departmentCode, moved || renamed || !match.IsActive ? PlannedChange.Update : PlannedChange.Unchanged, match.Id,
                Note(renamed ? $"renamed from {match.Name}" : null, moved ? $"moved from {currentDepartment}" : null, match.IsActive ? null : "reactivated")));
        }

        // ----- Roles and levels -------------------------------------------------------------------
        HashSet<string> levelCodes = [.. existing.ActiveLevels.Select(level => level.Code)];
        int longestLevel = existing.ActiveLevels.Count == 0 ? 0 : existing.ActiveLevels.Max(level => level.Code.Length);
        var roles = new Dictionary<string, (string Name, string Department, int Line)>(StringComparer.OrdinalIgnoreCase);

        foreach (StructureRow row in unique.Where(row => row.RoleName is not null || row.RoleCode is not null))
        {
            if (!departmentCodeByName.TryGetValue(row.Department!, out string? departmentCode))
            {
                continue;
            }

            if (row.RoleCode is null || row.RoleName is null)
            {
                errors.Add(new(row.Line, "IMPORT_ROLE_INCOMPLETE", "A role needs both a name and a code."));
                continue;
            }

            string code = Norm(row.RoleCode);
            // The role's designations take "-L1" etc. on the end, which must still fit.
            if (CodeProblem(code, longestLevel == 0 ? 0 : longestLevel + 1) is { } problem)
            {
                errors.Add(new(row.Line, "IMPORT_CODE_INVALID", $"Role code {row.RoleCode}: {problem}"));
                continue;
            }

            if (row.LevelCode is { } level && !levelCodes.Contains(Norm(level)))
            {
                errors.Add(new(row.Line, "IMPORT_LEVEL_UNKNOWN", $"There is no active level {level}. Levels in use: {string.Join(", ", levelCodes.Order())}."));
            }

            if (roles.TryGetValue(code, out var earlier))
            {
                if (!earlier.Department.Equals(departmentCode, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new(row.Line, "IMPORT_ROLE_TWO_DEPARTMENTS", $"The role {code} is in {departmentNameByCode[earlier.Department]} on line {earlier.Line} and in {row.Department} here."));
                }
                else if (!earlier.Name.Equals(row.RoleName, StringComparison.Ordinal))
                {
                    errors.Add(new(row.Line, "IMPORT_NAMES_DIFFER", $"The role {code} is called {earlier.Name} on line {earlier.Line} and {row.RoleName} here."));
                }

                continue;
            }

            roles[code] = (row.RoleName, departmentCode, row.Line);
        }

        Dictionary<string, ExistingUnit> rolesByCode = existing.Roles.ToDictionary(unit => unit.Code, StringComparer.OrdinalIgnoreCase);
        var plannedRoles = new List<PlannedRole>();
        var designationCodes = new HashSet<string>(existing.DesignationCodes, StringComparer.OrdinalIgnoreCase);
        int newDesignations = 0;

        foreach ((string code, (string name, string departmentCode, _)) in roles)
        {
            if (rolesByCode.TryGetValue(code, out ExistingUnit? match))
            {
                string? currentDepartment = match.ParentId is { } id ? departmentCodeById.GetValueOrDefault(id) : null;
                bool moved = !string.Equals(currentDepartment, departmentCode, StringComparison.OrdinalIgnoreCase);
                bool renamed = match.Name != name;
                plannedRoles.Add(new(code, name, departmentCode, moved || renamed || !match.IsActive ? PlannedChange.Update : PlannedChange.Unchanged, match.Id,
                    Note(renamed ? $"renamed from {match.Name}" : null, moved ? (currentDepartment is null ? "placed in its department" : $"moved from {currentDepartment}") : null, match.IsActive ? null : "reactivated")));
            }
            else
            {
                plannedRoles.Add(new(code, name, departmentCode, PlannedChange.Create, null, null));
            }

            newDesignations += existing.ActiveLevels.Count(level => designationCodes.Add($"{code}-{level.Code}"));
        }

        if (duplicates > 0)
        {
            warnings.Add(new(0, "IMPORT_DUPLICATES", $"{duplicates} repeated line(s) were read once."));
        }

        return new StructurePlan(
            rows.Count,
            duplicates,
            [.. departments.OrderBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)],
            [.. plannedFunctions.OrderBy(unit => unit.DepartmentCode, StringComparer.OrdinalIgnoreCase).ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)],
            [.. plannedRoles.OrderBy(unit => unit.DepartmentCode, StringComparer.OrdinalIgnoreCase).ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)],
            newDesignations,
            errors,
            warnings);
    }

    /// <summary>"Technology Services" → "D-TECSER": the first three letters of each word.</summary>
    public static string GeneratedDepartmentCode(string name)
    {
        string part = string.Concat(name.ToUpperInvariant()
            .Split([' ', '&', ',', '(', ')', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string([.. word.Where(char.IsAsciiLetterOrDigit)]))
            .Where(word => word.Length > 0)
            .Select(word => word.Length > 3 ? word[..3] : word));
        return OrgUnit.NormalizeCode($"D-{(part.Length > MaxCode - 2 ? part[..(MaxCode - 2)] : part)}");
    }

    /// <summary>Trimmed and upper-cased like a saved code, but never throws: a bad code becomes a row error instead.</summary>
    private static string Norm(string code) => code.Trim().ToUpperInvariant();

    private static string? CodeProblem(string code, int reserve = 0)
    {
        if (code.Any(char.IsWhiteSpace))
        {
            return "codes cannot contain spaces.";
        }

        if (code.Length > MaxCode - reserve)
        {
            return reserve == 0
                ? $"codes can be at most {MaxCode} characters."
                : $"at most {MaxCode - reserve} characters, so its designations (code-L1 …) still fit in {MaxCode}.";
        }

        return null;
    }

    private static string? Note(params string?[] parts)
    {
        string joined = string.Join("; ", parts.Where(part => part is not null));
        return joined.Length == 0 ? null : joined;
    }
}
