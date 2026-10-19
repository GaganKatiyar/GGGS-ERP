using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Helpdesk.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Helpdesk.Application;

/// <summary>Helpdesk for company-wide dashboards: open tickets, service targets met and satisfaction.</summary>
internal sealed class HelpdeskMetrics(HelpdeskService helpdesk) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Experience];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        HelpdeskDashboard dashboard = await helpdesk.DashboardAsync(cancellationToken);
        return
        [
            new Metric("experience.tickets", MetricAreas.Experience, "Open helpdesk tickets", dashboard.Open, MetricUnit.Count, $"{dashboard.Unassigned} unassigned",
                [.. dashboard.OpenByCategory.Select(pair => new MetricPoint(pair.Key, pair.Value))], HigherIsBetter: false),
            new Metric("experience.sla", MetricAreas.Experience, "Response target met", (decimal)dashboard.ResponseSlaMetPercent, MetricUnit.Percent,
                dashboard.AverageResolutionHours is { } hours ? $"Resolved in {hours} h on average" : null, Executive: true),
            new Metric("experience.satisfaction", MetricAreas.Experience, "Helpdesk satisfaction", (decimal)(dashboard.AverageSatisfaction ?? 0), MetricUnit.Score, "Out of 5"),
        ];
    }
}

/// <summary>Published knowledge articles in global search, for everyone who can raise tickets.</summary>
internal sealed class KnowledgeSearch(AppDbContext dbContext, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "article";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        if (!granted.Has(Permissions.Self.TicketCreate) && !granted.Has(Permissions.Helpdesk.KnowledgeManage))
        {
            return [];
        }

        var rows = await dbContext.KnowledgeArticles.AsNoTracking()
            .Where(row => row.IsPublished && (row.Title.Contains(term) || (row.Tags != null && row.Tags.Contains(term))))
            .OrderByDescending(row => row.Views).Take(take)
            .Select(row => new { row.Id, row.Title, row.Tags })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchHit(Kind, row.Id, row.Title, row.Tags, "/helpdesk/knowledge", row.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.6 : 0.35))];
    }
}
