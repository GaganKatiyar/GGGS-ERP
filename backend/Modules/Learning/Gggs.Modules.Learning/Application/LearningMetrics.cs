using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Learning.Application;

/// <summary>Learning for company-wide talent dashboards: completion, overdue work, mandatory compliance and certificates.</summary>
internal sealed class LearningMetrics(LearningService learning) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Talent];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        LearningDashboard dashboard = await learning.DashboardAsync(cancellationToken);
        return
        [
            new Metric("talent.compliance", MetricAreas.Talent, "Mandatory learning compliance", dashboard.MandatoryCompliancePercent ?? 0, MetricUnit.Percent, "People with every mandatory course done", Executive: true),
            new Metric("talent.learning", MetricAreas.Talent, "Courses completed (90 days)", dashboard.CompletedLast90Days, MetricUnit.Count, $"{dashboard.InProgress} in progress, {dashboard.Assigned} not started",
                [.. dashboard.TopCourses.Select(course => new MetricPoint(course.Title, course.Completed))]),
            new Metric("talent.learning-overdue", MetricAreas.Talent, "Overdue learning", dashboard.Overdue, MetricUnit.Count, $"{dashboard.CertificatesExpiringIn30Days} certificate(s) expiring in 30 days", HigherIsBetter: false),
        ];
    }
}
