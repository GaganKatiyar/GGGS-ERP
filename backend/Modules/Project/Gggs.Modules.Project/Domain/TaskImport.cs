using System.Globalization;
using System.Text;
using Gggs.BuildingBlocks.Domain.Exceptions;

namespace Gggs.Modules.Project.Domain;

public sealed record ImportPerson(Guid Id, string Name, string? Email, string? Code);

public sealed record ImportColumn(Guid Id, string Name, ColumnCategory Category);

public sealed record ImportExisting(Guid Id, string Code, string? ExternalKey, TaskType Type);

/// <summary>One line of the file, as it would become a task. Errors keep it out; warnings say what was changed to fit it in.</summary>
public sealed record ImportRow(
    int Line,
    string? Key,
    string Title,
    TaskType Type,
    WorkPriority Priority,
    string? Description,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid ColumnId,
    string ColumnName,
    string? ParentKey,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    int? StoryPoints,
    bool AlreadyImported,
    int Depth,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

/// <summary>
/// Reads a spreadsheet of tasks, or a Jira CSV export, into rows the project can take. Pure, so the preview
/// and the import cannot disagree. What will not fit is changed and reported rather than losing the whole file.
/// </summary>
public static class TaskImport
{
    public const int MaxCharacters = 2_000_000;
    public const int MaxRows = 1000;

    private static readonly string[] TitleHeaders = ["summary", "title", "name", "task"];
    private static readonly string[] KeyHeaders = ["issue key", "key", "external key", "code"];
    private static readonly string[] IdHeaders = ["issue id"];
    private static readonly string[] ParentHeaders = ["parent", "parent key", "epic link", "custom field (epic link)"];
    private static readonly string[] ParentIdHeaders = ["parent id"];
    private static readonly string[] TypeHeaders = ["issue type", "type"];
    private static readonly string[] PriorityHeaders = ["priority"];
    private static readonly string[] AssigneeHeaders = ["assignee", "assigned to", "owner"];
    private static readonly string[] StatusHeaders = ["status", "column"];
    private static readonly string[] DescriptionHeaders = ["description"];
    private static readonly string[] StartHeaders = ["start date", "start", "custom field (start date)"];
    private static readonly string[] DueHeaders = ["due date", "due", "duedate"];
    private static readonly string[] PointHeaders = ["story points", "points", "custom field (story points)", "custom field (story point estimate)"];
    private static readonly string[] HourHeaders = ["estimated hours", "estimate", "hours"];
    private static readonly string[] SecondsHeaders = ["original estimate", "σ original estimate"];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "dd/MMM/yy h:mm tt", "d/MMM/yy h:mm tt", "dd/MMM/yy", "d/MMM/yy", "dd/MMM/yyyy", "d/MMM/yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "d MMM yyyy", "dd MMM yyyy",
    ];

    /// <summary>A file's rows as text, quoted fields and all. The first is the header.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ReadCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        var field = new StringBuilder();
        bool quoted = false;
        int start = csv.Length > 0 && csv[0] == '﻿' ? 1 : 0;

        for (int index = start; index < csv.Length; index++)
        {
            char current = csv[index];
            if (quoted)
            {
                if (current != '"')
                {
                    field.Append(current);
                }
                else if (index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (current == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (current == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (current is '\n' or '\r')
            {
                if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Exists(value => value.Length > 0))
                {
                    rows.Add(row);
                }

                row = [];
            }
            else
            {
                field.Append(current);
            }
        }

        row.Add(field.ToString());
        if (row.Exists(value => value.Length > 0))
        {
            rows.Add(row);
        }

        return rows;
    }

    public static IReadOnlyList<ImportRow> Plan(string csv, IReadOnlyList<ImportPerson> people, IReadOnlyList<ImportColumn> columns, IReadOnlyList<ImportExisting> existing)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(existing);
        if (csv.Length > MaxCharacters)
        {
            throw new DomainRuleException("TASK_IMPORT_SIZE", "That file is too big. Split it into files under 2 MB.");
        }

        IReadOnlyList<IReadOnlyList<string>> rows = ReadCsv(csv);
        if (rows.Count == 0)
        {
            throw new DomainRuleException("TASK_IMPORT_EMPTY", "That file is empty.");
        }

        if (rows.Count - 1 > MaxRows)
        {
            throw new DomainRuleException("TASK_IMPORT_SIZE", $"That file has {rows.Count - 1} rows. Import up to {MaxRows} at a time.");
        }

        // Jira repeats a header for a multi-valued field, so each name can have several columns: the first filled one wins.
        ILookup<string, int> headers = rows[0].Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index)).ToLookup(pair => pair.Name, pair => pair.Index);
        if (!TitleHeaders.Any(headers.Contains))
        {
            throw new DomainRuleException("TASK_IMPORT_COLUMNS", "The file needs a Title or Summary column.");
        }

        if (columns.Count == 0)
        {
            throw new DomainRuleException("PROJECT_BOARD_SHAPE", "The board has no column for new work.");
        }

        Dictionary<string, ImportExisting> byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (ImportExisting task in existing)
        {
            byKey[task.Code] = task;
            if (task.ExternalKey is { } external)
            {
                byKey[external] = task;
            }
        }

        string? Value(IReadOnlyList<string> row, string[] names)
        {
            foreach (string name in names)
            {
                foreach (int index in headers[name])
                {
                    if (index < row.Count && !string.IsNullOrWhiteSpace(row[index]))
                    {
                        return row[index].Trim();
                    }
                }
            }

            return null;
        }

        // Jira's numeric parent ids are turned into keys, so everything below deals only in keys.
        Dictionary<string, string> keyOfId = new(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> row in rows.Skip(1))
        {
            if (Value(row, IdHeaders) is { } id && Value(row, KeyHeaders) is { } key)
            {
                keyOfId[id] = key;
            }
        }

        List<Draft> drafts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < rows.Count; index++)
        {
            IReadOnlyList<string> row = rows[index];
            List<string> warnings = [];
            List<string> errors = [];

            string? key = Value(row, KeyHeaders);
            string? title = Value(row, TitleHeaders);
            if (title is null)
            {
                errors.Add("No title.");
            }
            else if (title.Length > 300)
            {
                title = title[..300];
                warnings.Add("Title shortened to 300 characters.");
            }

            if (key is not null && !seen.Add(key))
            {
                errors.Add($"{key} appears more than once in the file.");
            }

            string? description = Value(row, DescriptionHeaders);
            if (description is { Length: > 8000 })
            {
                description = description[..8000];
                warnings.Add("Description shortened to 8,000 characters.");
            }

            ImportPerson? assignee = null;
            if (Value(row, AssigneeHeaders) is { } who)
            {
                assignee = people.FirstOrDefault(person => Same(person.Name, who) || Same(person.Email, who) || Same(person.Code, who));
                if (assignee is null)
                {
                    warnings.Add($"{who} is not on this project's team, so it is left unassigned.");
                }
            }

            DateOnly? start = ReadDate(Value(row, StartHeaders), "start date", warnings);
            DateOnly? due = ReadDate(Value(row, DueHeaders), "due date", warnings);
            if (start is { } from && due is { } to && to < from)
            {
                start = null;
                warnings.Add("The due date is before the start date, so the start date is left out.");
            }

            string? parent = Value(row, ParentHeaders);
            if (parent is null && Value(row, ParentIdHeaders) is { } parentId)
            {
                parent = keyOfId.GetValueOrDefault(parentId);
                if (parent is null)
                {
                    warnings.Add($"Parent {parentId} is not in this file, so it is left with no parent.");
                }
            }

            ImportColumn column = ColumnFor(Value(row, StatusHeaders), columns, warnings);
            drafts.Add(new Draft(
                index + 1, key, title ?? string.Empty, ReadType(Value(row, TypeHeaders), warnings), ReadPriority(Value(row, PriorityHeaders), warnings), description,
                assignee, column, parent, start, due, ReadHours(row, Value, warnings), ReadPoints(Value(row, PointHeaders), warnings),
                key is not null && byKey.TryGetValue(key, out ImportExisting? done) && done.ExternalKey is not null && string.Equals(done.ExternalKey, key, StringComparison.OrdinalIgnoreCase),
                warnings, errors));
        }

        return Resolve(drafts, byKey);
    }

    /// <summary>Works out parents, the order to create them in, and whether each row's place in the hierarchy is allowed.</summary>
    private static List<ImportRow> Resolve(List<Draft> drafts, Dictionary<string, ImportExisting> existing)
    {
        Dictionary<string, Draft> inFile = new(StringComparer.OrdinalIgnoreCase);
        foreach (Draft draft in drafts.Where(draft => draft.Key is not null && draft.Errors.Count == 0))
        {
            inFile[draft.Key!] = draft;
        }

        foreach (Draft draft in drafts)
        {
            if (draft.ParentKey is not null && !inFile.ContainsKey(draft.ParentKey) && !existing.ContainsKey(draft.ParentKey))
            {
                draft.Warnings.Add($"Parent {draft.ParentKey} is not in this file or project, so it is left with no parent.");
                draft.ParentKey = null;
            }

            // Walking up finds a loop, which would otherwise never finish.
            HashSet<Draft> path = [draft];
            for (Draft? step = Parent(draft); step is not null; step = Parent(step))
            {
                if (!path.Add(step))
                {
                    draft.Warnings.Add("Its parents loop back to it, so it is left with no parent.");
                    draft.ParentKey = null;
                    break;
                }
            }
        }

        foreach (Draft draft in drafts)
        {
            TaskType? parentType = draft.ParentKey is null
                ? null
                : inFile.TryGetValue(draft.ParentKey, out Draft? parent) ? parent.Type : existing[draft.ParentKey].Type;
            if (TaskHierarchy.CanHaveParent(draft.Type, parentType))
            {
                continue;
            }

            if (parentType is not null && TaskHierarchy.CanHaveParent(draft.Type, null))
            {
                draft.Warnings.Add($"A {TaskHierarchy.Describe(draft.Type)} cannot sit under a {TaskHierarchy.Describe(parentType.Value)}, so it is left with no parent.");
                draft.ParentKey = null;
            }
            else if (draft.Type == TaskType.Subtask && parentType is null)
            {
                draft.Warnings.Add("A subtask needs a parent, so it comes in as a task.");
                draft.Type = TaskType.Task;
            }
            else
            {
                draft.Warnings.Add($"A {TaskHierarchy.Describe(draft.Type)} cannot sit under a {(parentType is null ? "nothing" : TaskHierarchy.Describe(parentType.Value))}, so it comes in as a task with no parent.");
                draft.Type = TaskType.Task;
                draft.ParentKey = null;
            }
        }

        return [.. drafts.Select(draft =>
        {
            int depth = 0;
            for (Draft? step = Parent(draft); step is not null; step = Parent(step))
            {
                depth++;
            }

            return new ImportRow(
                draft.Line, draft.Key, draft.Title, draft.Type, draft.Priority, draft.Description, draft.Assignee?.Id, draft.Assignee?.Name, draft.Column.Id, draft.Column.Name,
                draft.ParentKey, draft.StartDate, draft.DueDate, draft.EstimatedHours, draft.StoryPoints, draft.AlreadyImported, depth, draft.Warnings, draft.Errors);
        })];

        Draft? Parent(Draft draft) => draft.ParentKey is { } key && inFile.TryGetValue(key, out Draft? found) ? found : null;
    }

    private static bool Same(string? known, string given) => known is not null && string.Equals(known.Trim(), given, StringComparison.OrdinalIgnoreCase);

    private static TaskType ReadType(string? value, List<string> warnings)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "task" or "technical task" or "improvement" or "chore" or "spike":
                return TaskType.Task;
            case "epic":
                return TaskType.Epic;
            case "story" or "user story" or "new feature" or "feature":
                return TaskType.Story;
            case "sub-task" or "subtask" or "sub task":
                return TaskType.Subtask;
            case "bug" or "defect":
                return TaskType.Bug;
            case "change" or "change request" or "changerequest":
                return TaskType.ChangeRequest;
            case "incident":
                return TaskType.Incident;
            case "service request" or "servicerequest" or "request":
                return TaskType.ServiceRequest;
            case "experiment":
                return TaskType.Experiment;
            default:
                warnings.Add($"Type {value} is not known here, so it comes in as a task.");
                return TaskType.Task;
        }
    }

    private static WorkPriority ReadPriority(string? value, List<string> warnings)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "highest" or "blocker" or "critical" or "urgent" or "p1":
                return WorkPriority.Critical;
            case "high" or "major" or "p2":
                return WorkPriority.High;
            case null or "medium" or "normal" or "p3":
                return WorkPriority.Medium;
            case "low" or "lowest" or "minor" or "trivial" or "p4" or "p5":
                return WorkPriority.Low;
            default:
                warnings.Add($"Priority {value} is not known here, so it comes in as medium.");
                return WorkPriority.Medium;
        }
    }

    private static ImportColumn ColumnFor(string? status, IReadOnlyList<ImportColumn> columns, List<string> warnings)
    {
        ImportColumn Fallback() => columns.FirstOrDefault(column => column.Category == ColumnCategory.Backlog)
            ?? columns.FirstOrDefault(column => column.Category != ColumnCategory.Done)
            ?? columns[0];

        if (status is null)
        {
            return Fallback();
        }

        if (columns.FirstOrDefault(column => Same(column.Name, status)) is { } named)
        {
            return named;
        }

        ColumnCategory? category = status.Trim().ToLowerInvariant() switch
        {
            "done" or "closed" or "resolved" or "complete" or "completed" or "released" => ColumnCategory.Done,
            "in progress" or "in development" or "in review" or "code review" or "testing" or "qa" or "doing" => ColumnCategory.InProgress,
            "to do" or "todo" or "open" or "selected for development" or "ready" => ColumnCategory.Todo,
            "backlog" => ColumnCategory.Backlog,
            _ => null,
        };
        if (category is { } wanted && columns.FirstOrDefault(column => column.Category == wanted) is { } matched)
        {
            return matched;
        }

        ImportColumn fallback = Fallback();
        warnings.Add($"Status {status} is not on this board, so it goes to {fallback.Name}.");
        return fallback;
    }

    private static DateOnly? ReadDate(string? value, string what, List<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return DateOnly.FromDateTime(parsed);
        }

        warnings.Add($"The {what} {value} could not be read, so it is left out.");
        return null;
    }

    private static decimal? ReadHours(IReadOnlyList<string> row, Func<IReadOnlyList<string>, string[], string?> value, List<string> warnings)
    {
        decimal? hours = null;
        if (value(row, HourHeaders) is { } typed)
        {
            if (decimal.TryParse(typed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
            {
                hours = parsed;
            }
            else
            {
                warnings.Add($"The estimate {typed} could not be read, so it is left out.");
            }
        }
        else if (value(row, SecondsHeaders) is { } seconds && decimal.TryParse(seconds, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal total))
        {
            // Jira exports estimates in seconds.
            hours = Math.Round(total / 3600, 2);
        }

        if (hours is < 0 or > 10_000)
        {
            warnings.Add("The estimate is outside 0 to 10,000 hours, so it is left out.");
            return null;
        }

        return hours;
    }

    private static int? ReadPoints(string? value, List<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) && parsed is >= 0 and <= 1000)
        {
            return (int)Math.Round(parsed);
        }

        warnings.Add($"The story points {value} could not be used, so they are left out.");
        return null;
    }

    private sealed class Draft(
        int line,
        string? key,
        string title,
        TaskType type,
        WorkPriority priority,
        string? description,
        ImportPerson? assignee,
        ImportColumn column,
        string? parentKey,
        DateOnly? startDate,
        DateOnly? dueDate,
        decimal? estimatedHours,
        int? storyPoints,
        bool alreadyImported,
        List<string> warnings,
        List<string> errors)
    {
        public int Line { get; } = line;

        public string? Key { get; } = key;

        public string Title { get; } = title;

        public TaskType Type { get; set; } = type;

        public WorkPriority Priority { get; } = priority;

        public string? Description { get; } = description;

        public ImportPerson? Assignee { get; } = assignee;

        public ImportColumn Column { get; } = column;

        public string? ParentKey { get; set; } = parentKey;

        public DateOnly? StartDate { get; } = startDate;

        public DateOnly? DueDate { get; } = dueDate;

        public decimal? EstimatedHours { get; } = estimatedHours;

        public int? StoryPoints { get; } = storyPoints;

        public bool AlreadyImported { get; } = alreadyImported;

        public List<string> Warnings { get; } = warnings;

        public List<string> Errors { get; } = errors;
    }
}
