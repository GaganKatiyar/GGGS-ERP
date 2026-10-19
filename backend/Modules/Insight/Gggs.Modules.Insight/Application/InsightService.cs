using System.Globalization;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Insight.Domain;
using Gggs.Modules.Insight.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Insight.Application;

public sealed record AreaInfo(string Key, string Title, string Description);

public sealed record DashboardView(string Area, string Title, DateTime GeneratedAt, bool TenantWide, IReadOnlyList<Metric> Metrics);

public sealed record SearchResults(string Term, IReadOnlyList<SearchHit> Hits);

public sealed record CalendarView(DateOnly FirstDay, DateOnly LastDay, string Scope, IReadOnlyList<CalendarEntry> Entries);

public sealed record SubscriptionItem(Guid Id, string Area, string Title, DigestFrequency Frequency, DateOnly? LastSentOn);

public sealed record SubscriptionBody(string Area, DigestFrequency Frequency);

/// <summary>
/// Analytics and dashboards (spec: analytics, executive dashboards): every module measures its own
/// data through <see cref="IMetricSource"/>; this assembles them into one dashboard per area, and a
/// company-wide executive view, within the caller's report access.
/// </summary>
internal sealed class AnalyticsService(
    IEnumerable<IMetricSource> sources,
    IEmployeeDirectory employees,
    IPermissionProvider permissions,
    ITenantClock clock,
    TimeProvider timeProvider,
    ILogger<AnalyticsService> logger)
{
    public const string Executive = "executive";

    private static readonly Dictionary<string, AreaInfo> Areas = new(StringComparer.OrdinalIgnoreCase)
    {
        [Executive] = new(Executive, "Executive", "Company-wide headline figures across people, delivery and money."),
        [MetricAreas.People] = new(MetricAreas.People, "People", "Headcount, joiners, exits, attrition, tenure and diversity."),
        [MetricAreas.Workforce] = new(MetricAreas.Workforce, "Workforce", "Positions, vacancies and planned hiring."),
        [MetricAreas.Time] = new(MetricAreas.Time, "Time", "Attendance, leave and timesheets."),
        [MetricAreas.Projects] = new(MetricAreas.Projects, "Projects", "Portfolio health, delivery and profitability."),
        [MetricAreas.Resources] = new(MetricAreas.Resources, "Resources", "Utilization, bench and allocation."),
        [MetricAreas.Payroll] = new(MetricAreas.Payroll, "Payroll", "Payroll cost, reimbursements and statutory payments."),
        [MetricAreas.Hiring] = new(MetricAreas.Hiring, "Hiring", "Requisitions, pipeline, offers and onboarding."),
        [MetricAreas.Talent] = new(MetricAreas.Talent, "Talent", "Goals, reviews, learning and promotions."),
        [MetricAreas.Experience] = new(MetricAreas.Experience, "Experience", "Engagement, recognition, helpdesk, expenses and assets."),
    };

    public static string TitleOf(string area) => Areas.TryGetValue(area, out AreaInfo? info) ? info.Title : area;

    public async Task<IReadOnlyList<AreaInfo>> AreasAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        return [.. Areas.Values.Where(area => area.Key == Executive ? granted.Has(Permissions.Report.ExecutiveView) : granted.Has(Permissions.Report.View))];
    }

    public async Task<DashboardView> DashboardAsync(string area, CancellationToken cancellationToken)
    {
        if (!Areas.ContainsKey(area))
        {
            throw new NotFoundException("Dashboard", area);
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope;
        if (area == Executive)
        {
            if (!granted.Has(Permissions.Report.ExecutiveView))
            {
                throw new ForbiddenException("REPORT_EXECUTIVE", "The executive dashboard needs executive report access.");
            }

            scope = null;
        }
        else
        {
            scope = await employees.InScopeIdsAsync(Permissions.Report.View, cancellationToken);
        }

        return await BuildAsync(area, scope, cancellationToken);
    }

    /// <summary>Builds a dashboard for a given reach - tenant-wide (null) or a set of employees.</summary>
    public async Task<DashboardView> BuildAsync(string area, IReadOnlyCollection<Guid>? scope, CancellationToken cancellationToken)
    {
        var context = new MetricContext(scope, await clock.TodayAsync(cancellationToken));
        List<Metric> metrics = [];

        // One DbContext per request: sources run one after another, never in parallel.
        foreach (IMetricSource source in sources.Where(source => area == Executive || source.Areas.Contains(area, StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                IReadOnlyList<Metric> collected = await source.CollectAsync(context, cancellationToken);
                metrics.AddRange(collected.Where(metric => area == Executive ? metric.Executive : string.Equals(metric.Area, area, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Metric source {Source} failed for the {Area} dashboard.", source.GetType().Name, area);
            }
        }

        return new DashboardView(area, TitleOf(area), timeProvider.GetUtcNow().UtcDateTime, scope is null, metrics);
    }
}

/// <summary>Global search (spec: global search): each module searches what the caller may see in it.</summary>
internal sealed class SearchService(IEnumerable<ISearchSource> sources, ILogger<SearchService> logger)
{
    public const int PerKind = 6;

    public async Task<SearchResults> SearchAsync(string? term, CancellationToken cancellationToken)
    {
        string text = (term ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            return new SearchResults(text, []);
        }

        text = text.Length > 100 ? text[..100] : text;
        List<SearchHit> hits = [];
        foreach (ISearchSource source in sources)
        {
            try
            {
                hits.AddRange((await source.SearchAsync(text, PerKind, cancellationToken)).Take(PerKind));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Search source {Kind} failed.", source.Kind);
            }
        }

        return new SearchResults(text, [.. hits.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Title, StringComparer.OrdinalIgnoreCase).Take(40)]);
    }

    /// <summary>How well a value matches a term: exact, then prefix, then word prefix, then contains.</summary>
    public static double Score(string? value, string term)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        if (value.Equals(term, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (value.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 0.8;
        }

        return value.Contains(" " + term, StringComparison.OrdinalIgnoreCase) ? 0.6 : value.Contains(term, StringComparison.OrdinalIgnoreCase) ? 0.4 : 0;
    }
}

/// <summary>The HR calendar (spec: HR calendar): holidays, leave, birthdays, joinings and exits, milestones, interviews, reviews and surveys.</summary>
internal sealed class CalendarService(
    IEnumerable<ICalendarSource> sources,
    IEmployeeDirectory employees,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ILogger<CalendarService> logger)
{
    public async Task<CalendarView> EntriesAsync(DateOnly firstDay, DateOnly lastDay, string? scope, CancellationToken cancellationToken)
    {
        if (lastDay < firstDay || lastDay.DayNumber - firstDay.DayNumber > 92)
        {
            throw new DomainRuleException("CALENDAR_RANGE", "Show up to three months at a time.");
        }

        string wanted = scope is "team" or "all" ? scope : "mine";
        CalendarContext context;
        if (wanted == "mine")
        {
            context = new CalendarContext(currentUser.EmployeeId, currentUser.EmployeeId is { } me ? [me] : [], OnlyMine: true);
        }
        else
        {
            PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
            IReadOnlyCollection<Guid>? reach = granted.Has(Permissions.Employee.View) ? await employees.InScopeIdsAsync(Permissions.Employee.View, cancellationToken) : [];
            if (wanted == "team")
            {
                // A team view is people in scope, and always oneself.
                List<Guid> team = reach is null ? [.. await employees.ListActiveIdsAsync(null, null, cancellationToken)] : [.. reach];
                if (currentUser.EmployeeId is { } me && !team.Contains(me))
                {
                    team.Add(me);
                }

                context = new CalendarContext(currentUser.EmployeeId, team, OnlyMine: false);
            }
            else
            {
                if (reach is not null)
                {
                    throw new ForbiddenException("CALENDAR_ALL", "The company calendar needs company-wide employee access.");
                }

                context = new CalendarContext(currentUser.EmployeeId, null, OnlyMine: false);
            }
        }

        List<CalendarEntry> entries = [];
        foreach (ICalendarSource source in sources)
        {
            try
            {
                entries.AddRange(await source.EntriesAsync(firstDay, lastDay, context, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Calendar source {Source} failed.", source.GetType().Name);
            }
        }

        return new CalendarView(firstDay, lastDay, wanted, [.. entries.Where(entry => entry.End >= firstDay && entry.Start <= lastDay).OrderBy(entry => entry.Start).ThenBy(entry => entry.Kind, StringComparer.Ordinal)]);
    }
}

/// <summary>Scheduled report digests: a dashboard's headline figures sent weekly or monthly.</summary>
internal sealed class DigestService(
    AppDbContext dbContext,
    AnalyticsService analytics,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    public async Task<IReadOnlyList<SubscriptionItem>> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.UserId;
        return [.. (await dbContext.ReportSubscriptions.AsNoTracking().Where(row => row.UserId == me).OrderBy(row => row.Area).ToListAsync(cancellationToken))
            .Select(row => new SubscriptionItem(row.Id, row.Area, AnalyticsService.TitleOf(row.Area), row.Frequency, row.LastSentOn))];
    }

    public async Task<IReadOnlyList<SubscriptionItem>> SubscribeAsync(SubscriptionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Area != AnalyticsService.Executive && !MetricAreas.All.Contains(body.Area))
        {
            throw new DomainRuleException("DIGEST_AREA", "Choose a dashboard.");
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        if (body.Area == AnalyticsService.Executive && !granted.Has(Permissions.Report.ExecutiveView))
        {
            throw new ForbiddenException("REPORT_EXECUTIVE", "The executive dashboard needs executive report access.");
        }

        Guid me = currentUser.UserId;
        ReportSubscription? existing = await dbContext.ReportSubscriptions.FirstOrDefaultAsync(row => row.UserId == me && row.Area == body.Area, cancellationToken);
        if (existing is null)
        {
            dbContext.ReportSubscriptions.Add(ReportSubscription.Create(me, currentUser.EmployeeId, body.Area, body.Frequency));
        }
        else
        {
            existing.Change(body.Frequency);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await MineAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionItem>> UnsubscribeAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid me = currentUser.UserId;
        ReportSubscription subscription = await dbContext.ReportSubscriptions.FirstOrDefaultAsync(row => row.Id == id && row.UserId == me, cancellationToken) ?? throw new NotFoundException(nameof(ReportSubscription), id);
        dbContext.ReportSubscriptions.Remove(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MineAsync(cancellationToken);
    }

    /// <summary>
    /// Sends the digests due today. Digests carry company-wide figures, so they go only to people
    /// whose report access is company-wide (or who hold executive access) at the time of sending.
    /// </summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ReportSubscription> due = [.. (await dbContext.ReportSubscriptions.ToListAsync(cancellationToken)).Where(row => row.IsDue(today))];
        int sent = 0;
        Dictionary<string, DashboardView> built = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReportSubscription subscription in due)
        {
            PermissionSet granted = await permissions.GetForUserAsync(subscription.UserId, cancellationToken);
            bool allowed = subscription.Area == AnalyticsService.Executive
                ? granted.Has(Permissions.Report.ExecutiveView)
                : granted.ScopeFor(Permissions.Report.View) == DataScope.Tenant;
            if (!allowed)
            {
                continue;
            }

            if (!built.TryGetValue(subscription.Area, out DashboardView? dashboard))
            {
                dashboard = await analytics.BuildAsync(subscription.Area, null, cancellationToken);
                built[subscription.Area] = dashboard;
            }

            string summary = string.Join("; ", dashboard.Metrics.Take(8).Select(metric => $"{metric.Label}: {Format(metric)}"));
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ReportDigest,
                    [new NotificationRecipient(subscription.EmployeeId, subscription.UserId)],
                    new Dictionary<string, string>
                    {
                        ["period"] = subscription.Frequency == DigestFrequency.Weekly ? "week" : "month",
                        ["dashboard"] = dashboard.Title.ToLowerInvariant(),
                        ["summary"] = summary.Length == 0 ? "nothing to report yet." : summary + ".",
                    },
                    subscription.Area == AnalyticsService.Executive ? "/analytics/executive" : $"/analytics/{subscription.Area}"),
                cancellationToken);
            subscription.Sent(today);
            sent++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sent;
    }

    public static string Format(Metric metric) => metric.Unit switch
    {
        MetricUnit.Percent => $"{metric.Value.ToString("0.#", CultureInfo.InvariantCulture)}%",
        MetricUnit.Currency => metric.Value.ToString("N0", CultureInfo.InvariantCulture),
        MetricUnit.Hours => $"{metric.Value.ToString("0.#", CultureInfo.InvariantCulture)} h",
        MetricUnit.Days => $"{metric.Value.ToString("0.#", CultureInfo.InvariantCulture)} days",
        _ => metric.Value.ToString("0.##", CultureInfo.InvariantCulture),
    };
}

public sealed record ImportHandlerInfo(string Key, string Title, string Description, IReadOnlyList<ImportColumn> Columns);

public sealed record ImportBody(string Csv, string? FileName);

public sealed record ImportRunItem(Guid Id, string HandlerKey, string FileName, int Rows, int Valid, int Imported, bool Committed, DateTime CreatedDate);

/// <summary>
/// The import framework (spec: import and export): one CSV pipeline - parse, validate every row,
/// preview, commit - for every bulk import a module offers.
/// </summary>
internal sealed class ImportService(AppDbContext dbContext, IEnumerable<IImportHandler> handlers, IPermissionProvider permissions, ICurrentUser currentUser)
{
    public const int MaxRows = 5000;

    public async Task<IReadOnlyList<ImportHandlerInfo>> HandlersAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        return [.. handlers.Where(handler => granted.Has(handler.Permission)).OrderBy(handler => handler.Title, StringComparer.Ordinal).Select(handler => new ImportHandlerInfo(handler.Key, handler.Title, handler.Description, handler.Columns))];
    }

    public async Task<string> TemplateAsync(string key, CancellationToken cancellationToken)
    {
        IImportHandler handler = await HandlerAsync(key, cancellationToken);
        return string.Join(',', handler.Columns.Select(column => Quote(column.Name))) + "\n" + string.Join(',', handler.Columns.Select(column => Quote(column.Example ?? string.Empty))) + "\n";
    }

    public async Task<ImportOutcome> RunAsync(string key, ImportBody body, bool commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        IImportHandler handler = await HandlerAsync(key, cancellationToken);
        List<Dictionary<string, string>> rows = Parse(body.Csv);
        if (rows.Count == 0)
        {
            throw new DomainRuleException("IMPORT_EMPTY", "The file has a header row but no data.");
        }

        if (rows.Count > MaxRows)
        {
            throw new DomainRuleException("IMPORT_TOO_LARGE", $"Import up to {MaxRows} rows at a time.");
        }

        string[] missing = [.. handler.Columns.Where(column => column.Required && !rows[0].ContainsKey(column.Name)).Select(column => column.Name)];
        if (missing.Length > 0)
        {
            throw new DomainRuleException("IMPORT_COLUMNS", $"The file is missing the column(s): {string.Join(", ", missing)}.");
        }

        ImportOutcome outcome = await handler.RunAsync(rows, commit, cancellationToken);
        if (commit)
        {
            dbContext.ImportRuns.Add(ImportRun.Record(handler.Key, body.FileName, outcome.Rows, outcome.Valid, outcome.Imported, committed: true, currentUser.UserId));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return outcome;
    }

    public async Task<IReadOnlyList<ImportRunItem>> HistoryAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.ImportRuns.AsNoTracking().OrderByDescending(row => row.CreatedDate).Take(100).ToListAsync(cancellationToken))
            .Select(row => new ImportRunItem(row.Id, row.HandlerKey, row.FileName, row.Rows, row.Valid, row.Imported, row.Committed, row.CreatedDate))];

    /// <summary>Parses CSV (RFC 4180: quoted fields, doubled quotes, line breaks inside quotes) into rows keyed by header.</summary>
    public static List<Dictionary<string, string>> Parse(string? csv)
    {
        List<List<string>> records = [];
        List<string> current = [];
        var field = new StringBuilder();
        bool quoted = false;
        string text = (csv ?? string.Empty).TrimStart('﻿');
        for (int index = 0; index < text.Length; index++)
        {
            char c = text[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    current.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    current.Add(field.ToString());
                    field.Clear();
                    records.Add(current);
                    current = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            records.Add(current);
        }

        records.RemoveAll(record => record.All(string.IsNullOrWhiteSpace));
        if (records.Count == 0)
        {
            return [];
        }

        string[] header = [.. records[0].Select(name => name.Trim())];
        return [.. records.Skip(1).Select(record =>
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int column = 0; column < header.Length; column++)
            {
                if (header[column].Length > 0)
                {
                    row[header[column]] = column < record.Count ? record[column].Trim() : string.Empty;
                }
            }

            return row;
        })];
    }

    private static string Quote(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private async Task<IImportHandler> HandlerAsync(string key, CancellationToken cancellationToken)
    {
        IImportHandler handler = handlers.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException("Import", key);
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(handler.Permission))
        {
            throw new ForbiddenException("IMPORT_PERMISSION", $"Importing {handler.Title.ToLowerInvariant()} needs the right to create them.");
        }

        return handler;
    }
}

public sealed record AdapterInfo(string Key, string Name, string Category, string Description, IReadOnlyList<IntegrationField> Fields, IReadOnlyList<IntegrationOperation> Operations);

public sealed record ConnectionItem(Guid Id, string AdapterKey, string AdapterName, string Name, IReadOnlyDictionary<string, string> Settings, bool IsActive, DateTime? LastTestedAt, bool? LastTestSucceeded, string? LastMessage);

public sealed record ConnectionBody(string AdapterKey, string Name, IReadOnlyDictionary<string, string>? Settings, bool IsActive = true);

public sealed record RunBody(string Operation, IReadOnlyDictionary<string, string>? Parameters);

public sealed record RunOutcome(bool Succeeded, string Message, int Records, string? FileName, string? ContentType, string? ContentBase64);

public sealed record RunItem(Guid Id, Guid ConnectionId, string Operation, bool Succeeded, string Message, int Records, string? FileName, DateTime CreatedDate);

/// <summary>
/// Integrations (spec: integrations): connections to outside systems through registered adapters,
/// with secret settings encrypted at rest and never returned, a connection test, and on-demand
/// operations whose runs are logged.
/// </summary>
internal sealed class IntegrationService(
    AppDbContext dbContext,
    IEnumerable<IIntegrationAdapter> adapters,
    ISensitiveDataProtector protector,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const string SecretPurpose = "integration.secret";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<AdapterInfo> Adapters()
        => [.. adapters.OrderBy(adapter => adapter.Category, StringComparer.Ordinal).ThenBy(adapter => adapter.Name, StringComparer.Ordinal)
            .Select(adapter => new AdapterInfo(adapter.Key, adapter.Name, adapter.Category, adapter.Description, adapter.Fields, adapter.Operations))];

    public async Task<IReadOnlyList<ConnectionItem>> ConnectionsAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.IntegrationConnections.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken)).Select(Item)];

    public async Task<ConnectionItem> SaveAsync(Guid? id, ConnectionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        IIntegrationAdapter adapter = Adapter(body.AdapterKey);
        string name = (body.Name ?? string.Empty).Trim();
        if (await dbContext.IntegrationConnections.AnyAsync(row => row.Name == name && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("INTEGRATION_NAME", $"A connection is already called {name}.");
        }

        IntegrationConnection? connection = id is { } existing
            ? await dbContext.IntegrationConnections.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(IntegrationConnection), existing)
            : null;
        Dictionary<string, StoredSetting> stored = connection is null ? [] : Stored(connection);
        Dictionary<string, StoredSetting> next = [];
        foreach (IntegrationField field in adapter.Fields)
        {
            string? given = body.Settings is not null && body.Settings.TryGetValue(field.Key, out string? value) ? value?.Trim() : null;
            if (field.Secret)
            {
                // A blank secret on update keeps the one already saved.
                if (!string.IsNullOrEmpty(given))
                {
                    ProtectedValue secret = protector.Protect(SecretPurpose, given, 2);
                    next[field.Key] = new StoredSetting(secret.Ciphertext, secret.Hint);
                }
                else if (stored.TryGetValue(field.Key, out StoredSetting? kept))
                {
                    next[field.Key] = kept;
                }
            }
            else if (!string.IsNullOrEmpty(given))
            {
                next[field.Key] = new StoredSetting(given, null);
            }

            if (field.Required && !next.ContainsKey(field.Key))
            {
                throw new DomainRuleException("INTEGRATION_SETTING", $"{field.Label} is required.");
            }
        }

        string json = JsonSerializer.Serialize(next, Json);
        if (connection is null)
        {
            connection = IntegrationConnection.Create(adapter.Key, name, json);
            dbContext.IntegrationConnections.Add(connection);
        }
        else
        {
            connection.Update(name, json, body.IsActive);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(connection);
    }

    public async Task<ConnectionItem> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        IntegrationConnection connection = await dbContext.IntegrationConnections.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(IntegrationConnection), id);
        IntegrationResult result;
        try
        {
            result = await Adapter(connection.AdapterKey).TestAsync(Revealed(connection), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            result = new IntegrationResult(false, exception.Message);
        }

        connection.Tested(result.Succeeded, result.Message, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(connection);
    }

    public async Task<RunOutcome> RunAsync(Guid id, RunBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        IntegrationConnection connection = await dbContext.IntegrationConnections.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(IntegrationConnection), id);
        if (!connection.IsActive)
        {
            throw new DomainRuleException("INTEGRATION_INACTIVE", "The connection is switched off.");
        }

        IIntegrationAdapter adapter = Adapter(connection.AdapterKey);
        IntegrationOperation operation = adapter.Operations.FirstOrDefault(candidate => candidate.Key == body.Operation)
            ?? throw new DomainRuleException("INTEGRATION_OPERATION", $"{adapter.Name} cannot {body.Operation}.");
        Dictionary<string, string> parameters = new(body.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (IntegrationField parameter in operation.Parameters.Where(parameter => parameter.Required && string.IsNullOrWhiteSpace(parameters.GetValueOrDefault(parameter.Key))))
        {
            throw new DomainRuleException("INTEGRATION_PARAMETER", $"{parameter.Label} is required.");
        }

        IntegrationResult result;
        try
        {
            result = await adapter.RunAsync(operation.Key, Revealed(connection), parameters, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            result = new IntegrationResult(false, exception.Message);
        }

        dbContext.IntegrationRuns.Add(IntegrationRun.Record(connection.Id, operation.Key, result.Succeeded, result.Message, result.Records, result.FileName, currentUser.UserId));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RunOutcome(result.Succeeded, result.Message, result.Records, result.FileName, result.ContentType, result.Content is null ? null : Convert.ToBase64String(result.Content));
    }

    public async Task<IReadOnlyList<RunItem>> RunsAsync(Guid? connectionId, CancellationToken cancellationToken)
    {
        IQueryable<IntegrationRun> rows = dbContext.IntegrationRuns.AsNoTracking();
        if (connectionId is { } id)
        {
            rows = rows.Where(row => row.ConnectionId == id);
        }

        return [.. (await rows.OrderByDescending(row => row.CreatedDate).Take(100).ToListAsync(cancellationToken))
            .Select(row => new RunItem(row.Id, row.ConnectionId, row.Operation, row.Succeeded, row.Message, row.Records, row.FileName, row.CreatedDate))];
    }

    private static Dictionary<string, StoredSetting> Stored(IntegrationConnection connection)
        => JsonSerializer.Deserialize<Dictionary<string, StoredSetting>>(connection.SettingsJson, Json) ?? [];

    private IIntegrationAdapter Adapter(string key)
        => adapters.FirstOrDefault(adapter => string.Equals(adapter.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw new DomainRuleException("INTEGRATION_ADAPTER", $"There is no {key} integration.");

    private Dictionary<string, string> Revealed(IntegrationConnection connection)
        => Stored(connection).ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Hint is null ? pair.Value.Value : protector.Reveal(SecretPurpose, new ProtectedValue(pair.Value.Value, pair.Value.Hint)),
            StringComparer.OrdinalIgnoreCase);

    private ConnectionItem Item(IntegrationConnection connection)
    {
        IIntegrationAdapter? adapter = adapters.FirstOrDefault(candidate => string.Equals(candidate.Key, connection.AdapterKey, StringComparison.OrdinalIgnoreCase));
        return new ConnectionItem(
            connection.Id,
            connection.AdapterKey,
            adapter?.Name ?? connection.AdapterKey,
            connection.Name,
            Stored(connection).ToDictionary(pair => pair.Key, pair => pair.Value.Hint ?? pair.Value.Value),
            connection.IsActive,
            connection.LastTestedAt,
            connection.LastTestSucceeded,
            connection.LastMessage);
    }

    /// <summary>A saved setting: plain value, or ciphertext with its masked hint for secrets.</summary>
    private sealed record StoredSetting(string Value, string? Hint);
}
