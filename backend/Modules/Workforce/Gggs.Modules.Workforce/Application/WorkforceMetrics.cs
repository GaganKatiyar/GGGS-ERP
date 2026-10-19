using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Workforce.Application;

/// <summary>Workforce planning for company-wide dashboards: the approved plan against live headcount, open positions and budgets.</summary>
internal sealed class WorkforceMetrics(WorkforceService workforce) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Workforce];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        WorkforceDashboard dashboard = await workforce.DashboardAsync(cancellationToken);
        return
        [
            new Metric("workforce.plan", MetricAreas.Workforce, "Planned headcount", dashboard.Planned, MetricUnit.Count, dashboard.Plan is null ? "No approved plan" : $"{dashboard.Live} live today",
                [.. dashboard.Departments.Select(row => new MetricPoint(row.DepartmentName ?? "Unknown", row.Planned))]),
            new Metric("workforce.gap", MetricAreas.Workforce, "Gap to plan", dashboard.Gap, MetricUnit.Count, "Planned less live",
                [.. dashboard.Departments.Where(row => row.Gap != 0).OrderByDescending(row => row.Gap).Select(row => new MetricPoint(row.DepartmentName ?? "Unknown", row.Gap))], Executive: true, HigherIsBetter: false),
            new Metric("workforce.open-positions", MetricAreas.Workforce, "Open positions", dashboard.OpenPositions, MetricUnit.Count, $"{dashboard.PendingRequests} manpower request(s) pending",
                [.. dashboard.RequestsByStatus.Select(pair => new MetricPoint(pair.Key, pair.Value))]),
            new Metric("workforce.budget", MetricAreas.Workforce, "Salary budget", dashboard.SalaryBudget, MetricUnit.Currency, $"{dashboard.HiringBudget:N0} for hiring"),
        ];
    }
}
