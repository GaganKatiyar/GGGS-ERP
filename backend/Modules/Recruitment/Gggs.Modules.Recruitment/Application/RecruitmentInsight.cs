using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Recruitment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Application;

/// <summary>Hiring analytics for company-wide dashboards: open roles, the pipeline, offers and time to hire this quarter.</summary>
internal sealed class RecruitmentMetrics(RecruitmentService recruitment) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Hiring];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        DateOnly today = context.Today;
        var quarterStart = new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
        RecruitmentAnalytics analytics = await recruitment.AnalyticsAsync(quarterStart, today, cancellationToken);
        return
        [
            new Metric("hiring.open", MetricAreas.Hiring, "Open requisitions", analytics.OpenRequisitions, MetricUnit.Count, $"{analytics.OpenPositions} position(s) to fill", Executive: true),
            new Metric("hiring.pipeline", MetricAreas.Hiring, "Applications this quarter", analytics.Applications, MetricUnit.Count, null,
                [.. analytics.Pipeline.Select(row => new MetricPoint(row.Label, row.Count))]),
            new Metric("hiring.hires", MetricAreas.Hiring, "Hires this quarter", analytics.Hires, MetricUnit.Count, null,
                [.. analytics.Sources.OrderByDescending(row => row.Hired).Select(row => new MetricPoint(row.Source, row.Hired))], Executive: true),
            new Metric("hiring.time-to-hire", MetricAreas.Hiring, "Average days to hire", analytics.AverageDaysToHire ?? 0, MetricUnit.Days, analytics.CostPerHire is { } cost ? $"{cost:N0} per hire" : null, HigherIsBetter: false),
            new Metric("hiring.offer-acceptance", MetricAreas.Hiring, "Offer acceptance", analytics.OfferAcceptancePercent ?? 0, MetricUnit.Percent, $"{analytics.OffersAccepted} accepted, {analytics.OffersDeclined} declined"),
        ];
    }
}

/// <summary>Candidates and requisitions in global search, for those who see applications.</summary>
internal sealed class CandidateSearch(AppDbContext dbContext, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "candidate";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        List<SearchHit> hits = [];
        if (granted.Has(Permissions.Recruitment.ApplicationView))
        {
            var candidates = await dbContext.Candidates.AsNoTracking()
                .Where(row => row.FirstName.Contains(term) || (row.LastName != null && row.LastName.Contains(term)) || row.Email.Contains(term) || row.Number.Contains(term))
                .OrderBy(row => row.FirstName).Take(take)
                .Select(row => new { row.Id, row.FirstName, row.LastName, row.Number, row.CurrentTitle })
                .ToListAsync(cancellationToken);
            hits.AddRange(candidates.Select(row => new SearchHit("candidate", row.Id, $"{row.FirstName} {row.LastName}".Trim(), $"{row.Number}{(row.CurrentTitle is null ? string.Empty : " · " + row.CurrentTitle)}",
                $"/recruitment/candidates/{row.Id}", row.FirstName.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.7 : 0.45)));
        }

        if (granted.Has(Permissions.Recruitment.RequisitionView))
        {
            var requisitions = await dbContext.Requisitions.AsNoTracking()
                .Where(row => row.Title.Contains(term) || row.Number.Contains(term))
                .OrderBy(row => row.Title).Take(take)
                .Select(row => new { row.Id, row.Title, row.Number })
                .ToListAsync(cancellationToken);
            hits.AddRange(requisitions.Select(row => new SearchHit("requisition", row.Id, row.Title, row.Number, $"/recruitment/requisitions/{row.Id}",
                row.Number.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1 : row.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.7 : 0.45)));
        }

        return hits;
    }
}

/// <summary>Interviews on the HR calendar: those one sits on, or every interview for recruiters.</summary>
internal sealed class InterviewCalendar(AppDbContext dbContext, IPermissionProvider permissions, ICurrentUser currentUser) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool recruiter = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Recruitment.ApplicationView);
        DateTime start = firstDay.ToDateTime(TimeOnly.MinValue);
        DateTime end = lastDay.ToDateTime(TimeOnly.MaxValue);
        var rows = await (
                from interview in dbContext.Interviews.AsNoTracking()
                join application in dbContext.JobApplications.AsNoTracking() on interview.ApplicationId equals application.Id
                join candidate in dbContext.Candidates.AsNoTracking() on application.CandidateId equals candidate.Id
                where interview.ScheduledAt >= start && interview.ScheduledAt <= end && interview.Status == InterviewStatus.Scheduled
                select new { interview.Id, interview.Round, interview.ScheduledAt, interview.PanelJson, candidate.FirstName, candidate.LastName })
            .Take(500).ToListAsync(cancellationToken);
        Guid? me = currentUser.EmployeeId;
        string mine = me?.ToString() ?? "-";
        return [.. rows
            .Where(row => (context.OnlyMine || !recruiter) ? row.PanelJson.Contains(mine, StringComparison.OrdinalIgnoreCase) : true)
            .Select(row => new CalendarEntry(CalendarKinds.Interview, $"Interview: {row.FirstName} {row.LastName} ({row.Round})", DateOnly.FromDateTime(row.ScheduledAt), DateOnly.FromDateTime(row.ScheduledAt), null,
                row.PanelJson.Contains(mine, StringComparison.OrdinalIgnoreCase) ? "/me/interviews" : "/recruitment/interviews", row.ScheduledAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC"))];
    }
}
