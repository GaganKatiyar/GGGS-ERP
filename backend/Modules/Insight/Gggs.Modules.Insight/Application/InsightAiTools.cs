using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Insight.Application;

/// <summary>Dashboard figures for an area, within the caller's report access.</summary>
internal sealed class GetMetricsTool(AnalyticsService analytics) : IAiTool
{
    public string Name => "get_metrics";

    public string Description => $"Headline figures, breakdowns and trends for one analytics area: {string.Join(", ", MetricAreas.All)}, or executive. Use for questions about headcount, attrition, leave, utilization, payroll cost, hiring pipeline, reviews, engagement and the like.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("area", "string", "One of: executive, " + string.Join(", ", MetricAreas.All), true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Report.View;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string area = (AiJson.Text(input, "area") ?? MetricAreas.People).Trim().ToLowerInvariant();
        DashboardView view = await analytics.DashboardAsync(area, cancellationToken);
        return AiToolOutput.Data(new
        {
            view.Title,
            view.TenantWide,
            Metrics = view.Metrics.Select(metric => new
            {
                metric.Label,
                Value = DigestService.Format(metric),
                metric.Hint,
                Breakdown = metric.Breakdown?.Take(8).Select(point => $"{point.Label}: {point.Value:0.##}"),
                Trend = metric.Trend?.TakeLast(6).Select(point => $"{point.Label}: {point.Value:0.##}"),
            }),
        });
    }
}

/// <summary>Global search, as the search box does it.</summary>
internal sealed class SearchDirectoryTool(SearchService search) : IAiTool
{
    public string Name => "search_directory";

    public string Description => "Finds people, projects, clients, candidates, requisitions, assets and help articles by name, code or keyword - only what this person may see.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("query", "string", "Name, code or keyword (at least 2 characters)", true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => null;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        SearchResults results = await search.SearchAsync(AiJson.Text(input, "query"), cancellationToken);
        return AiToolOutput.Data(results.Hits.Take(15).Select(hit => new { hit.Kind, hit.Title, hit.Subtitle }));
    }
}

/// <summary>What is coming up on the person's calendar.</summary>
internal sealed class UpcomingCalendarTool(CalendarService calendar, TimeProvider timeProvider) : IAiTool
{
    public string Name => "upcoming_calendar";

    public string Description => "Upcoming holidays, leave, birthdays, anniversaries, joinings, last days, milestones, interviews, review deadlines and survey closings. Scope 'mine' (default) or 'team'.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("days", "integer", "How many days ahead, 1 to 90 (default 14)", false), ("scope", "string", "mine or team", false));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => null;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        int days = Math.Clamp(AiJson.Number(input, "days") ?? 14, 1, 90);
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        CalendarView view = await calendar.EntriesAsync(today, today.AddDays(days), AiJson.Text(input, "scope") == "team" ? "team" : "mine", cancellationToken);
        return AiToolOutput.Data(view.Entries.Take(40).Select(entry => new { entry.Kind, entry.Title, entry.Start, entry.End }));
    }
}
