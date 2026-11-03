namespace Gggs.Modules.Project.Domain;

/// <summary>The shared work a default project starts with: an activity, and what can be booked under it.</summary>
public sealed record DefaultActivity(string Name, IReadOnlyList<string> Tasks);

/// <summary>
/// The rules of default projects. Everyone can book to the default project of the department they sit
/// in, which is worked out rather than stored: no team row, so nobody counts as allocated and the bench
/// stays true, and a transfer needs nothing done.
/// </summary>
public static class DefaultProjects
{
    /// <summary>The code of the organisation's own default project, the last resort for any department without one.</summary>
    public const string OrganizationCode = "GENERAL";

    public const int CodeLength = 16;

    /// <summary>Default projects run until someone closes them.</summary>
    public static DateOnly OpenEnded { get; } = new(2099, 12, 31);

    public static IReadOnlyList<DefaultActivity> Activities { get; } =
    [
        new("Meetings", ["Stand-up meetings", "Ad hoc meetings", "Planned meetings"]),
        new("Administration", ["Internal administration", "HR and company activities"]),
        new("Learning", ["Training", "Knowledge sharing"]),
        new("Bench", ["Awaiting assignment"]),
    ];

    /// <summary>
    /// The default project for a department: its own, else the nearest parent's, else the organisation's.
    /// Without a department, the organisation's. The walk is bounded so a cycle in the data cannot hang it.
    /// </summary>
    public static Guid? Resolve(
        Guid? departmentId,
        IReadOnlyDictionary<Guid, Guid?> parents,
        IReadOnlyDictionary<Guid, Guid> departmentDefaults,
        Guid? organizationDefault)
    {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(departmentDefaults);

        Guid? current = departmentId;
        for (int depth = 0; depth < 12 && current is { } id; depth++)
        {
            if (departmentDefaults.TryGetValue(id, out Guid project))
            {
                return project;
            }

            current = parents.GetValueOrDefault(id);
        }

        return organizationDefault;
    }

    /// <summary>A project code for a department: its code cleaned to what a project code allows, made unique among those taken.</summary>
    public static string CodeFor(string departmentCode, IReadOnlySet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(departmentCode);
        ArgumentNullException.ThrowIfNull(taken);

        string cleaned = new([.. departmentCode.ToUpperInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character == '_')]);
        string stem = "D_" + (cleaned.Length == 0 ? "DEPT" : cleaned);
        stem = stem[..Math.Min(stem.Length, CodeLength)];
        if (!taken.Contains(stem))
        {
            return stem;
        }

        for (int suffix = 2; ; suffix++)
        {
            string tail = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string candidate = stem[..Math.Min(stem.Length, CodeLength - tail.Length)] + tail;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
