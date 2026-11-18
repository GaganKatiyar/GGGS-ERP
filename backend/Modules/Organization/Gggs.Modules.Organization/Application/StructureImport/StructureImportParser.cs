using System.Text;

namespace Gggs.Modules.Organization.Application.StructureImport;

/// <summary>
/// One line of an org-structure file: a department, optionally one of its functions, optionally a
/// role of the department at a level. Empty cells and the text <c>NULL</c> both read as "none".
/// </summary>
public sealed record StructureRow(
    int Line,
    string? Department,
    string? DepartmentCode,
    string? FunctionName,
    string? FunctionCode,
    string? RoleName,
    string? RoleCode,
    string? LevelCode);

/// <summary>A problem with the file or one of its lines. <see cref="Line"/> is 0 for the file as a whole.</summary>
public sealed record StructureIssue(int Line, string Code, string Message);

public sealed record StructureParseResult(IReadOnlyList<StructureRow> Rows, IReadOnlyList<StructureIssue> Errors);

/// <summary>
/// Reads an org-structure file: CSV, or a Markdown table (lines of <c>| a | b |</c>), with the
/// columns Department, FunctionName, FunctionCode, RoleName, RoleCode, RoleLevelCode and an optional
/// DepartmentCode. Header names are matched without regard to case, spaces or underscores, and a few
/// short forms are accepted (Function, Role, Level).
/// </summary>
public static class StructureImportParser
{
    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["department"] = nameof(StructureRow.Department),
        ["departmentname"] = nameof(StructureRow.Department),
        ["departmentcode"] = nameof(StructureRow.DepartmentCode),
        ["function"] = nameof(StructureRow.FunctionName),
        ["functionname"] = nameof(StructureRow.FunctionName),
        ["functioncode"] = nameof(StructureRow.FunctionCode),
        ["role"] = nameof(StructureRow.RoleName),
        ["rolename"] = nameof(StructureRow.RoleName),
        ["rolecode"] = nameof(StructureRow.RoleCode),
        ["level"] = nameof(StructureRow.LevelCode),
        ["levelcode"] = nameof(StructureRow.LevelCode),
        ["rolelevelcode"] = nameof(StructureRow.LevelCode),
    };

    private static readonly string[] Required =
    [
        nameof(StructureRow.Department), nameof(StructureRow.FunctionName), nameof(StructureRow.FunctionCode),
        nameof(StructureRow.RoleName), nameof(StructureRow.RoleCode), nameof(StructureRow.LevelCode),
    ];

    public static StructureParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        bool markdown = lines.Any(line => line.TrimStart().StartsWith('|'));

        var table = new List<(int Line, string[] Cells)>();
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (markdown)
            {
                string trimmed = line.Trim();
                // Only table lines count; the title, totals and the |---| separator are skipped.
                if (!trimmed.StartsWith('|') || IsSeparator(trimmed))
                {
                    continue;
                }

                table.Add((index + 1, SplitMarkdown(trimmed)));
            }
            else
            {
                table.Add((index + 1, SplitCsv(line)));
            }
        }

        if (table.Count == 0)
        {
            return new([], [new StructureIssue(0, "IMPORT_EMPTY", "The file has no rows.")]);
        }

        (int headerLine, string[] header) = table[0];
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int column = 0; column < header.Length; column++)
        {
            string key = new([.. header[column].Where(char.IsLetterOrDigit)]);
            if (Columns.TryGetValue(key, out string? field) && !positions.ContainsKey(field))
            {
                positions[field] = column;
            }
        }

        string[] missing = [.. Required.Where(field => !positions.ContainsKey(field))];
        if (missing.Length > 0)
        {
            return new([], [new StructureIssue(headerLine, "IMPORT_COLUMNS_MISSING", $"The header is missing: {string.Join(", ", missing.Select(Label))}.")]);
        }

        var rows = new List<StructureRow>(table.Count - 1);
        foreach ((int line, string[] cells) in table.Skip(1))
        {
            string? Cell(string field) => positions.TryGetValue(field, out int column) && column < cells.Length ? Clean(cells[column]) : null;

            rows.Add(new StructureRow(
                line,
                Cell(nameof(StructureRow.Department)),
                Cell(nameof(StructureRow.DepartmentCode)),
                Cell(nameof(StructureRow.FunctionName)),
                Cell(nameof(StructureRow.FunctionCode)),
                Cell(nameof(StructureRow.RoleName)),
                Cell(nameof(StructureRow.RoleCode)),
                Cell(nameof(StructureRow.LevelCode))));
        }

        return new(rows, []);
    }

    private static string Label(string field) => field switch
    {
        nameof(StructureRow.LevelCode) => "RoleLevelCode",
        _ => field,
    };

    private static string? Clean(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static bool IsSeparator(string line) => line.All(character => character is '|' or '-' or ':' or ' ' or '\t');

    private static string[] SplitMarkdown(string line)
    {
        string inner = line.Trim('|');
        return [.. inner.Split('|').Select(cell => cell.Trim())];
    }

    /// <summary>One CSV line: commas separate, double quotes wrap a cell, and "" inside quotes is a quote.</summary>
    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        cells.Add(current.ToString());
        return [.. cells];
    }
}
