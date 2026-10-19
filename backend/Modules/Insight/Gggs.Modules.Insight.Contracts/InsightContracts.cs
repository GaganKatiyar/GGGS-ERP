namespace Gggs.Modules.Insight.Contracts;

/// <summary>The analytics areas a metric belongs to, each a dashboard of its own.</summary>
public static class MetricAreas
{
    public const string People = "people";
    public const string Workforce = "workforce";
    public const string Time = "time";
    public const string Projects = "projects";
    public const string Resources = "resources";
    public const string Payroll = "payroll";
    public const string Hiring = "hiring";
    public const string Talent = "talent";
    public const string Experience = "experience";

    public static IReadOnlyList<string> All { get; } = [People, Workforce, Time, Projects, Resources, Payroll, Hiring, Talent, Experience];
}

public enum MetricUnit
{
    Count = 1,
    Percent = 2,
    Currency = 3,
    Hours = 4,
    Days = 5,
    Score = 6,
}

/// <summary>One labelled value in a breakdown or a trend.</summary>
public sealed record MetricPoint(string Label, decimal Value);

/// <summary>
/// One measure for a dashboard: a headline value, and optionally a breakdown (by department, by
/// status) or a trend (by month). <see cref="Executive"/> puts it on the executive dashboard too.
/// </summary>
public sealed record Metric(
    string Key,
    string Area,
    string Label,
    decimal Value,
    MetricUnit Unit,
    string? Hint = null,
    IReadOnlyList<MetricPoint>? Breakdown = null,
    IReadOnlyList<MetricPoint>? Trend = null,
    bool Executive = false,
    bool HigherIsBetter = true);

/// <summary>
/// Whom a metric covers: everyone (<see cref="EmployeeScope"/> null) or only the employees the
/// caller's report access reaches. Sources that cannot narrow to people return tenant-wide figures
/// only when <see cref="TenantWide"/> is true.
/// </summary>
public sealed record MetricContext(IReadOnlyCollection<Guid>? EmployeeScope, DateOnly Today)
{
    public bool TenantWide => EmployeeScope is null;
}

/// <summary>A module's contribution to analytics (ADR-12): it measures its own data and nothing else.</summary>
public interface IMetricSource
{
    /// <summary>The areas this source reports on; it is asked only for dashboards that show them.</summary>
    IReadOnlyCollection<string> Areas { get; }

    Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default);
}

/// <summary>One search result, with where it opens.</summary>
public sealed record SearchHit(string Kind, Guid Id, string Title, string? Subtitle, string Link, double Score);

/// <summary>
/// A module's contribution to global search. It searches only what the caller may see, checking
/// its own permissions, and returns nothing rather than failing when the caller sees nothing.
/// </summary>
public interface ISearchSource
{
    string Kind { get; }

    Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default);
}

/// <summary>What a calendar entry is about, for colour and filtering.</summary>
public static class CalendarKinds
{
    public const string Holiday = "holiday";
    public const string Leave = "leave";
    public const string Birthday = "birthday";
    public const string Anniversary = "anniversary";
    public const string Joining = "joining";
    public const string Exit = "exit";
    public const string Milestone = "milestone";
    public const string Interview = "interview";
    public const string Review = "review";
    public const string Survey = "survey";
    public const string Training = "training";
}

public sealed record CalendarEntry(string Kind, string Title, DateOnly Start, DateOnly End, Guid? EmployeeId = null, string? Link = null, string? Detail = null);

/// <summary>Whose calendar: the caller's own items, their team's (people in scope), or everyone's.</summary>
public sealed record CalendarContext(Guid? EmployeeId, IReadOnlyCollection<Guid>? EmployeeScope, bool OnlyMine);

/// <summary>A module's contribution to the HR calendar.</summary>
public interface ICalendarSource
{
    Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default);
}

/// <summary>A column an import accepts.</summary>
public sealed record ImportColumn(string Name, string Description, bool Required, string? Example = null);

public sealed record ImportRowResult(int Row, IReadOnlyList<string> Errors, string? Summary = null);

public sealed record ImportOutcome(int Rows, int Valid, int Imported, IReadOnlyList<ImportRowResult> Results);

/// <summary>
/// A bulk import a module offers through the shared import framework: the framework parses the CSV,
/// the handler validates each row against its own rules and, on commit, writes the valid rows.
/// </summary>
public interface IImportHandler
{
    string Key { get; }

    string Title { get; }

    string Description { get; }

    /// <summary>The permission the caller needs to run this import.</summary>
    string Permission { get; }

    IReadOnlyList<ImportColumn> Columns { get; }

    Task<ImportOutcome> RunAsync(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool commit, CancellationToken cancellationToken = default);
}

/// <summary>A setting an integration needs, such as a URL, a tenant id or a secret.</summary>
public sealed record IntegrationField(string Key, string Label, bool Secret = false, bool Required = true, string? Hint = null);

/// <summary>An operation an integration can run on demand, with its parameters.</summary>
public sealed record IntegrationOperation(string Key, string Label, string Description, IReadOnlyList<IntegrationField> Parameters);

public sealed record IntegrationResult(bool Succeeded, string Message, string? FileName = null, string? ContentType = null, byte[]? Content = null, int Records = 0);

/// <summary>
/// An adapter to an outside system (spec: integrations). File-based adapters (bank transfers,
/// accounting journals) produce a file to hand over; connected adapters call the outside system
/// with the settings the tenant saved.
/// </summary>
public interface IIntegrationAdapter
{
    string Key { get; }

    string Name { get; }

    string Category { get; }

    string Description { get; }

    IReadOnlyList<IntegrationField> Fields { get; }

    IReadOnlyList<IntegrationOperation> Operations { get; }

    /// <summary>Checks the settings without changing anything outside.</summary>
    Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);

    Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
