using Gggs.Data;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Engagement.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Engagement.Application;

/// <summary>Engagement for company-wide dashboards: the latest eNPS, its trend and response rates.</summary>
internal sealed class EngagementMetrics(EngagementService engagement) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Experience];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        EngagementDashboard dashboard = await engagement.DashboardAsync(cancellationToken);
        return
        [
            new Metric("experience.enps", MetricAreas.Experience, "Employee NPS", (decimal)(dashboard.LatestEnps ?? 0), MetricUnit.Score, dashboard.LatestEnps is null ? "No survey with enough answers yet" : "-100 to 100",
                Trend: [.. dashboard.EnpsTrend.Where(point => point.Enps != null).Select(point => new MetricPoint(point.Title, (decimal)point.Enps!.Value))], Executive: true),
            new Metric("experience.response-rate", MetricAreas.Experience, "Survey response rate", (decimal)dashboard.AverageResponseRate, MetricUnit.Percent, $"{dashboard.OpenSurveys} survey(s) open"),
        ];
    }
}

/// <summary>Survey closing dates on the HR calendar.</summary>
internal sealed class SurveyCalendar(AppDbContext dbContext) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IQueryable<Survey> surveys = dbContext.Surveys.AsNoTracking().Where(row => (row.Status == SurveyStatus.Open || row.Status == SurveyStatus.Scheduled) && row.ClosesOn >= firstDay && row.ClosesOn <= lastDay);
        if (context.OnlyMine)
        {
            Guid? me = context.EmployeeId;
            List<Guid> invited = await dbContext.SurveyInvitations.AsNoTracking().Where(row => row.EmployeeId == me && row.RespondedAt == null).Select(row => row.SurveyId).ToListAsync(cancellationToken);
            surveys = surveys.Where(row => invited.Contains(row.Id));
        }

        return [.. (await surveys.ToListAsync(cancellationToken)).Select(row => new CalendarEntry(CalendarKinds.Survey, $"{row.Title} closes", row.ClosesOn, row.ClosesOn, null, context.OnlyMine ? "/me/surveys" : $"/engagement/surveys/{row.Id}"))];
    }
}
