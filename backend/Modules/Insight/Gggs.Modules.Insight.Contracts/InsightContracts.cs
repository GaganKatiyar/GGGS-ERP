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

    /// <summary>Whether automations may act through it. Off by default: an adapter that produces a file for someone to download, or only reads, has nothing to do unattended.</summary>
    bool Automatable => false;

    /// <summary>
    /// The channel it can carry for the tenant's notifications (<see cref="IntegrationChannels"/>), if
    /// any. Such an adapter has a <c>send</c> operation taking <c>to</c> plus, for email, <c>subject</c>
    /// and <c>body</c> or, for text messages, <c>text</c>.
    /// </summary>
    string? Channel => null;

    /// <summary>Checks the settings without changing anything outside.</summary>
    Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);

    Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}

/// <summary>The account to make. Holds a temporary password, so it never prints it.</summary>
public sealed record AccountRequest(string EmployeeCode, string FirstName, string? LastName, string DisplayName, string? PreferredLogin, string TemporaryPassword)
{
    public override string ToString() => $"AccountRequest {{ EmployeeCode = {EmployeeCode} }}";
}

/// <summary>What a directory did. Created is false when it found an account already there: that one keeps its own password.</summary>
public sealed record AccountOutcome(bool Succeeded, string Message, string? ExternalId = null, string? Login = null, bool Created = false);

/// <summary>An outside directory Nuts and Bolts can make accounts in. It disables them and never deletes them.</summary>
public interface IAccountAdapter : IIntegrationAdapter
{
    /// <summary>Finds the account already carrying the employee code, or makes one. Must leave the password out of its message.</summary>
    Task<AccountOutcome> CreateAccountAsync(IReadOnlyDictionary<string, string> settings, AccountRequest account, CancellationToken cancellationToken = default);

    Task<IntegrationResult> DisableAccountAsync(IReadOnlyDictionary<string, string> settings, string externalId, CancellationToken cancellationToken = default);
}

/// <summary>What happened when an operation ran on a saved connection.</summary>
public sealed record ConnectionRunResult(bool Succeeded, string Message);

/// <summary>A saved connection an automation can act through, as another module may see it: never its settings.</summary>
public sealed record ConnectionOption(Guid Id, string Name, string AdapterKey, string AdapterName, bool IsActive, IReadOnlyList<IntegrationOperation> Operations, string? Channel = null);

/// <summary>The ways of reaching people an adapter can carry.</summary>
public static class IntegrationChannels
{
    public const string Email = "email";

    /// <summary>Text messages to a phone: SMS or WhatsApp.</summary>
    public const string Sms = "sms";
}

/// <summary>
/// Runs an operation on one of the tenant's saved connections - settings decrypted, the run
/// logged - for modules that act through connections without seeing their secrets (Studio's
/// automations). A refusal (switched off, unknown operation, missing parameter) is a failed result,
/// not an exception, so the caller can record it and try again later.
/// </summary>
/// <summary>What an inbound webhook needs of a connection to check who sent it. Never returned to a browser.</summary>
public sealed record InboundConnection(string AdapterKey, string Secret)
{
    public override string ToString() => $"InboundConnection {{ AdapterKey = {AdapterKey} }}";
}

public interface IIntegrationConnections
{
    /// <summary>The active connection and its secret, for a module verifying what an outside system sent; null if there is none.</summary>
    Task<InboundConnection?> InboundAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>The saved connections to these adapters, whether or not automations can use them.</summary>
    Task<IReadOnlyList<ConnectionOption>> ConnectionsToAsync(IReadOnlyCollection<string> adapterKeys, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConnectionOption>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The saved connections to directories that can make accounts.</summary>
    Task<IReadOnlyList<ConnectionOption>> AccountConnectionsAsync(CancellationToken cancellationToken = default);

    Task<AccountOutcome> CreateAccountAsync(Guid connectionId, AccountRequest account, CancellationToken cancellationToken = default);

    Task<ConnectionRunResult> DisableAccountAsync(Guid connectionId, string externalId, CancellationToken cancellationToken = default);

    Task<ConnectionRunResult> RunAsync(Guid connectionId, string operation, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
