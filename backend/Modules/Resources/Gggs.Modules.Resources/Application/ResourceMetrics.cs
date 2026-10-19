using System.Globalization;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Resources.Application;

/// <summary>
/// Resourcing analytics from the resource dashboard and the utilization report. Those already apply
/// resource access, so this reports only on company-wide dashboards.
/// </summary>
internal sealed class ResourceMetrics(ResourceService resources, ResourceInsights insights) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Resources];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        ResourceDashboard dashboard = await resources.DashboardAsync(cancellationToken);
        UtilizationReport utilization = await insights.UtilizationAsync(null, null, UtilizationGrouping.Department, null, cancellationToken);
        return
        [
            new Metric("resources.utilization", MetricAreas.Resources, "Billable utilization (last month)", utilization.UtilizationPercent, MetricUnit.Percent, "Billable over available hours",
                [.. utilization.Rows.OrderByDescending(row => row.UtilizationPercent).Select(row => new MetricPoint(row.Name, row.UtilizationPercent))],
                [.. utilization.Trend.Select(point => new MetricPoint(point.Month.ToString("MMM yy", CultureInfo.InvariantCulture), point.UtilizationPercent))], Executive: true),
            new Metric("resources.bench", MetricAreas.Resources, "On bench", dashboard.OnBench, MetricUnit.Count, $"Costing {dashboard.BenchCostPerDay.ToString("N0", CultureInfo.InvariantCulture)} a day", Executive: true, HigherIsBetter: false),
            new Metric("resources.allocation", MetricAreas.Resources, "Fully allocated", dashboard.FullyAllocated, MetricUnit.Count, $"of {dashboard.TotalPeople} people",
                [new("Fully allocated", dashboard.FullyAllocated), new("Partly available", dashboard.PartiallyAvailable), new("On bench", dashboard.OnBench), new("Over-allocated", dashboard.Overallocated)]),
            new Metric("resources.overallocated", MetricAreas.Resources, "Over-allocated", dashboard.Overallocated, MetricUnit.Count, null, HigherIsBetter: false),
            new Metric("resources.releases", MetricAreas.Resources, "Rolling off (30 days)", dashboard.UpcomingReleases, MetricUnit.Count, $"{dashboard.OpenRequests} open resource request(s)"),
        ];
    }
}
