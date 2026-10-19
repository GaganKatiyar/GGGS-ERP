using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Rewards.Application;

/// <summary>Rewards for company-wide dashboards: recognition, points awarded and budgets used.</summary>
internal sealed class RewardMetrics(RewardService rewards) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Experience];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        RewardDashboard dashboard = await rewards.DashboardAsync(cancellationToken);
        return
        [
            new Metric("experience.recognition", MetricAreas.Experience, "Recognitions this month", dashboard.RecognitionsThisMonth, MetricUnit.Count, $"{dashboard.PendingApprovals} awaiting approval",
                [.. dashboard.TopRecipients.Select(row => new MetricPoint(row.Name ?? "Someone", row.Points))], Executive: true),
            new Metric("experience.points", MetricAreas.Experience, "Reward value awarded this year", dashboard.ValueAwardedThisYear, MetricUnit.Currency, $"{dashboard.PointsAwardedThisYear:N0} points; {dashboard.PointsOutstanding:N0} unspent"),
            new Metric("experience.reward-budget", MetricAreas.Experience, "Reward budget used", dashboard.Budgets.Sum(row => row.Points) == 0 ? 0 : Math.Round(100m * dashboard.Budgets.Sum(row => row.Used) / dashboard.Budgets.Sum(row => row.Points), 1), MetricUnit.Percent,
                $"{dashboard.PendingRedemptions} redemption(s) to fulfil", [.. dashboard.Budgets.Select(row => new MetricPoint(row.Department, (decimal)row.UsedPercent))]),
        ];
    }
}
