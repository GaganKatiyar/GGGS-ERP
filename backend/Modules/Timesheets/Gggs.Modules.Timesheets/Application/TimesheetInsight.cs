using System.Globalization;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Timesheets.Domain;
using Gggs.Modules.Timesheets.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Timesheets.Application;

/// <summary>Timesheet analytics: hours booked, billable share and what is waiting for approval.</summary>
internal sealed class TimesheetMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Time];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateOnly since = context.Today.AddDays(-7 * 12);
        IQueryable<Timesheet> query = dbContext.Timesheets.AsNoTracking().Where(row => row.WeekStart >= since);
        if (context.EmployeeScope is { } scope)
        {
            query = query.Where(row => scope.Contains(row.EmployeeId));
        }

        var sheets = await query.Select(row => new { row.WeekStart, row.Status, row.TotalHours, row.BillableHours, row.IsLate }).ToListAsync(cancellationToken);
        var monthStart = new DateOnly(context.Today.Year, context.Today.Month, 1);
        var month = sheets.Where(row => row.WeekStart >= monthStart.AddDays(-6)).ToList();
        decimal total = month.Sum(row => row.TotalHours);
        return
        [
            new Metric("time.timesheet-hours", MetricAreas.Time, "Hours booked this month", total, MetricUnit.Hours,
                total == 0 ? null : $"{Math.Round(100m * month.Sum(row => row.BillableHours) / total, 1)}% billable",
                Trend: [.. sheets.GroupBy(row => row.WeekStart).OrderBy(group => group.Key).Select(group => new MetricPoint(group.Key.ToString("d MMM", CultureInfo.InvariantCulture), group.Sum(row => row.TotalHours)))]),
            new Metric("time.timesheet-pending", MetricAreas.Time, "Timesheets awaiting approval", sheets.Count(row => row.Status == TimesheetStatus.Submitted), MetricUnit.Count,
                $"{sheets.Count(row => row.IsLate)} submitted late in 12 weeks", HigherIsBetter: false),
        ];
    }
}
