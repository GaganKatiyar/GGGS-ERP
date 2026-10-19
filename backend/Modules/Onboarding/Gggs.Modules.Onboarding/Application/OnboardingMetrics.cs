using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Onboarding.Application;

/// <summary>Onboarding for company-wide hiring dashboards: joiners in progress, upcoming and overdue tasks.</summary>
internal sealed class OnboardingMetrics(OnboardingService onboarding) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Hiring];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        OnboardingDashboard dashboard = await onboarding.DashboardAsync(cancellationToken);
        return
        [
            new Metric("hiring.onboarding", MetricAreas.Hiring, "Joiners onboarding", dashboard.Open, MetricUnit.Count, $"{dashboard.JoiningNext30Days} joining in 30 days, {dashboard.PreBoarding} pre-boarding"),
            new Metric("hiring.onboarding-overdue", MetricAreas.Hiring, "Overdue onboarding tasks", dashboard.OverdueTasks, MetricUnit.Count, $"{dashboard.CompletedLast30Days} onboarding(s) completed in 30 days",
                [.. dashboard.OverdueByCategory.Select(pair => new MetricPoint(pair.Key, pair.Value))], HigherIsBetter: false),
        ];
    }
}
