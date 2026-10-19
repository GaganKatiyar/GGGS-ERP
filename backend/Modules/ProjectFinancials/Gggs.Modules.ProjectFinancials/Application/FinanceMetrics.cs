using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.ProjectFinancials.Application;

/// <summary>Portfolio profitability for company-wide dashboards: revenue, cost, margin and unbilled work.</summary>
internal sealed class FinanceMetrics(FinanceService finance) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Projects];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        PortfolioFinance portfolio = await finance.PortfolioAsync(cancellationToken);
        return
        [
            new Metric("projects.revenue", MetricAreas.Projects, "Revenue recognised", portfolio.Revenue, MetricUnit.Currency, $"of {portfolio.ContractValue:N0} contracted",
                [.. portfolio.Projects.OrderByDescending(row => row.Revenue).Take(10).Select(row => new MetricPoint(row.Code, row.Revenue))], Executive: true),
            new Metric("projects.margin", MetricAreas.Projects, "Gross margin", portfolio.MarginPercent ?? 0, MetricUnit.Percent, $"{portfolio.GrossMargin:N0} on {portfolio.ActualCost:N0} cost", Executive: true),
            new Metric("projects.unbilled", MetricAreas.Projects, "Unbilled work", portfolio.Unbilled, MetricUnit.Currency, $"{portfolio.Billed:N0} billed", HigherIsBetter: false),
            new Metric("projects.loss-making", MetricAreas.Projects, "Loss-making projects", portfolio.LossMaking, MetricUnit.Count, $"{portfolio.OverBudget} over budget", HigherIsBetter: false),
        ];
    }
}
