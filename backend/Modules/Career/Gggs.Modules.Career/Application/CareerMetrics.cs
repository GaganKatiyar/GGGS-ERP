using Gggs.Data;
using Gggs.Modules.Career.Domain;
using Gggs.Modules.Career.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Career.Application;

/// <summary>Career analytics: promotions this year and how well critical roles are covered by successors.</summary>
internal sealed class CareerMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Talent];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyCollection<Guid>? scope = context.EmployeeScope;
        var yearStart = new DateOnly(context.Today.Year, 1, 1);
        var promotions = await dbContext.Promotions.AsNoTracking()
            .Where(row => row.EffectiveDate >= yearStart && (scope == null || scope.Contains(row.EmployeeId)))
            .Select(row => new { row.Status })
            .ToListAsync(cancellationToken);
        var plans = await dbContext.SuccessionPlans.AsNoTracking()
            .Where(row => scope == null || (row.IncumbentEmployeeId != null && scope.Contains(row.IncumbentEmployeeId.Value)))
            .Select(row => new { row.Criticality, row.SuccessorsJson })
            .ToListAsync(cancellationToken);
        return
        [
            new Metric("talent.promotions", MetricAreas.Talent, "Promotions this year", promotions.Count(row => row.Status is PromotionStatus.Applied or PromotionStatus.Approved), MetricUnit.Count,
                $"{promotions.Count(row => row.Status == PromotionStatus.PendingApproval)} awaiting approval"),
            new Metric("talent.succession", MetricAreas.Talent, "Roles with a successor", plans.Count(row => row.SuccessorsJson.Length > 2), MetricUnit.Count, $"of {plans.Count} succession plan(s)",
                [.. plans.GroupBy(row => row.Criticality).OrderByDescending(group => group.Key).Select(group => new MetricPoint(group.Key.ToString(), group.Count()))]),
        ];
    }
}
