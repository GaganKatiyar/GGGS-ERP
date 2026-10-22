using System.Globalization;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record CountItem(string Label, int Count);

/// <summary>A survey question's result: the average rating (1-5), or the share answering yes (0-100).</summary>
public sealed record SurveyScore(string Question, string Kind, double Value, int Responses);

public sealed record AttritionAnalytics(
    int Months,
    DateOnly From,
    DateOnly To,
    int Exits,
    int Headcount,
    double AttritionRate,
    int Voluntary,
    int Involuntary,
    int Natural,
    int Regretted,
    int Classified,
    double? RegrettedPercent,
    int EarlyExits,
    double EarlyExitPercent,
    double? NoticeServedPercent,
    double? AverageSettlementDays,
    int SettlementsLate,
    IReadOnlyList<CountItem> Reasons,
    IReadOnlyList<CountItem> Tenure,
    IReadOnlyList<CountItem> Departments,
    IReadOnlyList<CountItem> Managers,
    IReadOnlyList<CountItem> Trend,
    IReadOnlyList<SurveyScore> Survey);

/// <summary>
/// Attrition analytics (Separation v2 phase E): exits over a window within the caller's reach -
/// the annualised rate, voluntary against involuntary and natural, regretted losses, early
/// attrition, reasons, tenure, departments and managers, the monthly trend, notice served,
/// settlement turnaround against the legal deadline, and how leavers answered the exit survey.
/// </summary>
internal sealed class SeparationAnalytics(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    SeparationPolicyService policy,
    ExitSurveyService surveys,
    ITenantClock clock)
{
    public async Task<AttritionAnalytics> AttritionAsync(int months, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 1, 36);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        DateOnly from = monthStart.AddMonths(-(months - 1));
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Separation.AnalyticsView, cancellationToken);

        List<SeparationCase> exits = await dbContext.SeparationCases.AsNoTracking()
            .Where(row => (row.Status == CaseStatus.Completed || (row.Status == CaseStatus.Approved && row.LastWorkingDay < today))
                && row.LastWorkingDay >= from && row.LastWorkingDay <= today
                && (scope == null || scope.Contains(row.EmployeeId)))
            .ToListAsync(cancellationToken);
        List<Guid> caseIds = [.. exits.Select(row => row.Id)];
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(exits.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id);
        int headcount = (await employees.ListActiveIdsAsync(null, null, cancellationToken)).Count(id => scope is null || scope.Contains(id));

        // The usual monthly attrition rate (exits over average headcount), scaled to a year.
        double averageHeadcount = headcount + (exits.Count / 2.0);
        double rate = averageHeadcount == 0 ? 0 : Math.Round(exits.Count * 100.0 / averageHeadcount * 12 / months, 1);

        List<SeparationCase> voluntary = [.. exits.Where(row => row.Type == SeparationType.Resignation)];
        int involuntary = exits.Count(row => row.Type is SeparationType.Termination or SeparationType.Retrenchment or SeparationType.Absconding or SeparationType.Mutual);
        int classified = voluntary.Count(row => row.Regretted is not null);
        int regretted = voluntary.Count(row => row.Regretted == true);

        int TenureDays(SeparationCase row) => people.GetValueOrDefault(row.EmployeeId)?.JoiningDate is { } joined ? row.LastWorkingDay.DayNumber - joined.DayNumber : -1;
        int early = exits.Count(row => TenureDays(row) is >= 0 and < 183);

        double? noticeServed = voluntary.Count == 0 ? null : Math.Round(voluntary.Count(row => row.ShortfallDays == 0) * 100.0 / voluntary.Count, 1);

        // Settlement turnaround against the policy deadline (Sundays skipped; holidays not counted here).
        SeparationPolicy terms = await policy.PolicyAsync(cancellationToken);
        Dictionary<Guid, Settlement> settlements = await dbContext.Settlements.AsNoTracking().Where(row => caseIds.Contains(row.CaseId)).ToDictionaryAsync(row => row.CaseId, cancellationToken);
        List<int> turnaround = [];
        int late = 0;
        foreach (SeparationCase row in exits)
        {
            DateOnly due = SettlementMath.WorkingDaysAfter(row.LastWorkingDay, terms.SettlementDueWorkingDays, new HashSet<DateOnly>());
            Settlement? settlement = settlements.GetValueOrDefault(row.Id);
            if (settlement?.PaidOn is { } paid)
            {
                turnaround.Add(paid.DayNumber - row.LastWorkingDay.DayNumber);
                late += paid > due ? 1 : 0;
            }
            else if (today > due)
            {
                late++;
            }
        }

        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(
            OrgUnitKind.Department, people.Values.Where(person => person.DepartmentId != null).Select(person => person.DepartmentId!.Value).Distinct(), cancellationToken);
        Dictionary<Guid, string> managers = (await employees.GetManyAsync(people.Values.Where(person => person.ManagerEmployeeId != null).Select(person => person.ManagerEmployeeId!.Value).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        static List<CountItem> Count(IEnumerable<string> labels, int take = 12)
            => [.. labels.GroupBy(label => label).Select(group => new CountItem(group.Key, group.Count())).OrderByDescending(item => item.Count).ThenBy(item => item.Label, StringComparer.Ordinal).Take(take)];

        string Band(SeparationCase row) => TenureDays(row) switch
        {
            < 0 => "Unknown",
            < 183 => "Under 6 months",
            < 365 => "6-12 months",
            < 730 => "1-2 years",
            < 1826 => "2-5 years",
            _ => "5 years or more",
        };
        string[] bands = ["Under 6 months", "6-12 months", "1-2 years", "2-5 years", "5 years or more", "Unknown"];
        List<CountItem> tenure = [.. bands.Select(band => new CountItem(band, exits.Count(row => Band(row) == band))).Where(item => item.Count > 0 || item.Label != "Unknown")];

        List<CountItem> trend = [.. Enumerable.Range(0, months).Select(index =>
        {
            DateOnly month = from.AddMonths(index);
            return new CountItem(month.ToString("MMM yyyy", CultureInfo.InvariantCulture), exits.Count(row => row.LastWorkingDay.Year == month.Year && row.LastWorkingDay.Month == month.Month));
        })];

        (ExitSurvey survey, IReadOnlyList<ExitSurveyResponse> responses) = await surveys.ResponsesAsync(caseIds, cancellationToken);
        List<SurveyScore> scores = [];
        foreach (ExitQuestion question in survey.Questions.Where(question => question.Type is ExitQuestionType.Rating or ExitQuestionType.YesNo))
        {
            List<string> answers = [.. responses.Select(response => response.Answers.GetValueOrDefault(question.Key)).OfType<string>()];
            if (answers.Count == 0)
            {
                continue;
            }

            scores.Add(question.Type == ExitQuestionType.Rating
                ? new SurveyScore(question.Text, "rating", Math.Round(answers.Select(answer => int.TryParse(answer, out int value) ? value : 0).Where(value => value > 0).DefaultIfEmpty().Average(), 2), answers.Count)
                : new SurveyScore(question.Text, "yes", Math.Round(answers.Count(answer => answer == "yes") * 100.0 / answers.Count, 1), answers.Count));
        }

        return new AttritionAnalytics(
            months, from, today, exits.Count, headcount, rate,
            voluntary.Count, involuntary, exits.Count - voluntary.Count - involuntary,
            regretted, classified, classified == 0 ? null : Math.Round(regretted * 100.0 / classified, 1),
            early, exits.Count == 0 ? 0 : Math.Round(early * 100.0 / exits.Count, 1),
            noticeServed, turnaround.Count == 0 ? null : Math.Round(turnaround.Average(), 1), late,
            Count(exits.Select(row => SeparationReasons.LabelOf(row.ReasonCode) ?? "Not recorded")),
            tenure,
            Count(exits.Select(row => people.GetValueOrDefault(row.EmployeeId)?.DepartmentId is { } department ? departments.GetValueOrDefault(department) ?? "Unknown" : "No department")),
            Count(exits.Select(row => people.GetValueOrDefault(row.EmployeeId)?.ManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager) ?? "Unknown" : "No manager"), take: 10),
            trend,
            scores);
    }
}
