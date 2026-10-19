using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Performance.Domain;
using Gggs.Modules.Performance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Performance.Application;

/// <summary>Performance analytics: goal progress, review completion and the rating spread of the latest released cycle.</summary>
internal sealed class PerformanceMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Talent];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyCollection<Guid>? scope = context.EmployeeScope;
        var goals = await dbContext.Goals.AsNoTracking()
            .Where(row => row.Status == GoalStatus.Active && row.Level == GoalLevel.Individual && row.OwnerEmployeeId != null && (scope == null || scope.Contains(row.OwnerEmployeeId.Value)))
            .Select(row => new { row.Progress, row.DueDate })
            .ToListAsync(cancellationToken);
        List<ReviewCycle> cycles = await dbContext.ReviewCycles.AsNoTracking().Where(row => row.Status != CycleStatus.Draft).OrderByDescending(row => row.PeriodEnd).Take(6).ToListAsync(cancellationToken);
        ReviewCycle? running = cycles.FirstOrDefault(row => row.Status is CycleStatus.Active or CycleStatus.Calibration);
        ReviewCycle? released = cycles.FirstOrDefault(row => row.Status is CycleStatus.Released or CycleStatus.Closed);
        List<Guid> cycleIds = [.. new[] { running?.Id, released?.Id }.Where(id => id != null).Select(id => id!.Value)];
        var reviews = await dbContext.Reviews.AsNoTracking()
            .Where(row => cycleIds.Contains(row.CycleId) && (scope == null || scope.Contains(row.EmployeeId)))
            .Select(row => new { row.CycleId, row.Status, row.FinalRating })
            .ToListAsync(cancellationToken);
        int plans = await dbContext.ImprovementPlans.AsNoTracking().CountAsync(row => scope == null || scope.Contains(row.EmployeeId), cancellationToken);

        List<Metric> metrics =
        [
            new Metric("talent.goal-progress", MetricAreas.Talent, "Average goal progress", goals.Count == 0 ? 0 : Math.Round(goals.Average(row => row.Progress), 1), MetricUnit.Percent,
                $"{goals.Count} active individual goal(s), {goals.Count(row => row.DueDate < context.Today && row.Progress < 100)} past due"),
        ];
        if (running is not null)
        {
            var current = reviews.Where(row => row.CycleId == running.Id).ToList();
            int done = current.Count(row => row.Status >= ReviewStatus.ManagerSubmitted);
            metrics.Add(new Metric("talent.review-completion", MetricAreas.Talent, $"{running.Name}: reviews done", current.Count == 0 ? 0 : Math.Round(100m * done / current.Count, 1), MetricUnit.Percent,
                $"{done} of {current.Count} manager reviews in",
                [.. current.GroupBy(row => row.Status).OrderBy(group => group.Key).Select(group => new MetricPoint(group.Key.ToString(), group.Count()))], Executive: true));
        }

        if (released is not null)
        {
            var rated = reviews.Where(row => row.CycleId == released.Id && row.FinalRating != null).ToList();
            metrics.Add(new Metric("talent.ratings", MetricAreas.Talent, $"{released.Name}: average rating", rated.Count == 0 ? 0 : Math.Round((decimal)rated.Average(row => row.FinalRating!.Value), 2), MetricUnit.Score,
                $"{rated.Count} rated", [.. Enumerable.Range(1, 5).Select(rating => new MetricPoint($"{rating}", rated.Count(row => row.FinalRating == rating)))]));
        }

        metrics.Add(new Metric("talent.pips", MetricAreas.Talent, "Improvement plans", plans, MetricUnit.Count, null, HigherIsBetter: false));
        return metrics;
    }
}

/// <summary>Review deadlines on the HR calendar.</summary>
internal sealed class ReviewCalendar(AppDbContext dbContext) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<ReviewCycle> cycles = await dbContext.ReviewCycles.AsNoTracking()
            .Where(row => (row.Status == CycleStatus.Active || row.Status == CycleStatus.Calibration)
                && ((row.SelfReviewDue >= firstDay && row.SelfReviewDue <= lastDay) || (row.ManagerReviewDue >= firstDay && row.ManagerReviewDue <= lastDay)))
            .ToListAsync(cancellationToken);
        if (context.OnlyMine)
        {
            Guid? me = context.EmployeeId;
            List<Guid> ids = [.. cycles.Select(row => row.Id)];
            HashSet<Guid> mine = [.. await dbContext.Reviews.AsNoTracking().Where(row => ids.Contains(row.CycleId) && (row.EmployeeId == me || row.ManagerEmployeeId == me)).Select(row => row.CycleId).ToListAsync(cancellationToken)];
            cycles = [.. cycles.Where(row => mine.Contains(row.Id))];
        }

        List<CalendarEntry> entries = [];
        foreach (ReviewCycle cycle in cycles)
        {
            if (cycle.SelfReviewDue >= firstDay && cycle.SelfReviewDue <= lastDay)
            {
                entries.Add(new CalendarEntry(CalendarKinds.Review, $"{cycle.Name}: self-reviews due", cycle.SelfReviewDue, cycle.SelfReviewDue, null, context.OnlyMine ? "/me/reviews" : "/performance"));
            }

            if (cycle.ManagerReviewDue >= firstDay && cycle.ManagerReviewDue <= lastDay)
            {
                entries.Add(new CalendarEntry(CalendarKinds.Review, $"{cycle.Name}: manager reviews due", cycle.ManagerReviewDue, cycle.ManagerReviewDue, null, context.OnlyMine ? "/me/reviews" : "/performance"));
            }
        }

        return entries;
    }
}
