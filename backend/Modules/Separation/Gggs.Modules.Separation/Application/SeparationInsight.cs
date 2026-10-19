using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

/// <summary>Separations for people dashboards: who is leaving, why, and what is still open.</summary>
internal sealed class SeparationMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.People];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyCollection<Guid>? scope = context.EmployeeScope;
        DateOnly yearAgo = context.Today.AddYears(-1);
        var cases = await dbContext.SeparationCases.AsNoTracking()
            .Where(row => (row.Status == CaseStatus.Approved || row.Status == CaseStatus.PendingApproval || (row.Status == CaseStatus.Completed && row.LastWorkingDay >= yearAgo))
                && (scope == null || scope.Contains(row.EmployeeId)))
            .Select(row => new { row.Id, row.Status, row.Type, row.LastWorkingDay })
            .ToListAsync(cancellationToken);
        List<Guid> ids = [.. cases.Select(row => row.Id)];
        List<string> reasons = await dbContext.ExitInterviews.AsNoTracking().Where(row => ids.Contains(row.CaseId)).Select(row => row.PrimaryReason).ToListAsync(cancellationToken);
        return
        [
            new Metric("people.leaving", MetricAreas.People, "Leaving (in progress)", cases.Count(row => row.Status is CaseStatus.Approved or CaseStatus.PendingApproval), MetricUnit.Count,
                $"{cases.Count(row => row.Status == CaseStatus.Approved && row.LastWorkingDay <= context.Today.AddDays(30))} in the next 30 days",
                [.. cases.GroupBy(row => SeparationService.Label(row.Type)).Select(group => new MetricPoint(group.Key, group.Count()))], HigherIsBetter: false),
            new Metric("people.exit-reasons", MetricAreas.People, "Exit reasons (12 months)", reasons.Count, MetricUnit.Count, "From exit interviews",
                [.. reasons.GroupBy(reason => reason).OrderByDescending(group => group.Count()).Select(group => new MetricPoint(group.Key, group.Count()))]),
        ];
    }
}

/// <summary>Agreed last working days on the HR calendar, before the exit itself is recorded.</summary>
internal sealed class SeparationCalendar(AppDbContext dbContext, IEmployeeDirectory employees) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyCollection<Guid>? scope = context.EmployeeScope;
        var cases = await dbContext.SeparationCases.AsNoTracking()
            .Where(row => row.Status == CaseStatus.Approved && row.LastWorkingDay >= firstDay && row.LastWorkingDay <= lastDay && (scope == null || scope.Contains(row.EmployeeId)))
            .Select(row => new { row.Id, row.EmployeeId, row.LastWorkingDay })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(cases.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(row => row.Id, row => row.DisplayName);
        return [.. cases.Select(row => new CalendarEntry(CalendarKinds.Exit, $"{names.GetValueOrDefault(row.EmployeeId) ?? "Someone"}: last working day", row.LastWorkingDay, row.LastWorkingDay, row.EmployeeId,
            context.OnlyMine ? "/me/separation" : $"/separation/cases/{row.Id}"))];
    }
}
