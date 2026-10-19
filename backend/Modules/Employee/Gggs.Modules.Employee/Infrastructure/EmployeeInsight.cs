using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>People analytics: headcount, joiners, exits and attrition, tenure and the make-up of the workforce.</summary>
internal sealed class EmployeeMetrics(AppDbContext dbContext, IOrgStructure organization, IMasterDataLookup lookups) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.People];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IQueryable<EmployeeEntity> query = dbContext.Employees.AsNoTracking();
        if (context.EmployeeScope is { } scope)
        {
            query = query.Where(row => scope.Contains(row.Id));
        }

        var people = await query
            .Select(row => new { row.Id, row.Status, row.JoiningDate, row.ExitDate, row.DepartmentId, row.GenderCode, row.EmploymentTypeCode, row.WorkModeCode, row.ProbationEndDate, row.ConfirmationDate })
            .ToListAsync(cancellationToken);
        DateOnly today = context.Today;
        var active = people.Where(row => row.Status != EmployeeStatus.Exited).ToList();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        DateOnly yearAgo = today.AddYears(-1);

        List<DateOnly> monthEnds = [.. Enumerable.Range(0, 12).Select(offset => monthStart.AddMonths(offset - 11)).Select(start => start.AddMonths(1).AddDays(-1))];
        static string Label(DateOnly day) => day.ToString("MMM yy", System.Globalization.CultureInfo.InvariantCulture);
        int HeadcountOn(DateOnly day) => people.Count(row => row.JoiningDate <= day && (row.ExitDate is null || row.ExitDate > day));

        List<MetricPoint> headcountTrend = [.. monthEnds.Select(end => new MetricPoint(Label(end), HeadcountOn(end <= today ? end : today)))];
        List<MetricPoint> joinTrend = [.. monthEnds.Select(end => new MetricPoint(Label(end), people.Count(row => row.JoiningDate.Year == end.Year && row.JoiningDate.Month == end.Month)))];
        List<MetricPoint> exitTrend = [.. monthEnds.Select(end => new MetricPoint(Label(end), people.Count(row => row.ExitDate is { } exit && exit.Year == end.Year && exit.Month == end.Month)))];
        int exits12 = people.Count(row => row.ExitDate is { } exit && exit > yearAgo && exit <= today);
        decimal averageHeadcount = headcountTrend.Count == 0 ? 0 : headcountTrend.Average(point => point.Value);
        decimal attrition = averageHeadcount == 0 ? 0 : Math.Round(100m * exits12 / averageHeadcount, 1);
        decimal tenure = active.Count == 0 ? 0 : Math.Round((decimal)active.Average(row => (today.DayNumber - row.JoiningDate.DayNumber) / 365.25), 1);

        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, active.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct(), cancellationToken);
        async Task<List<MetricPoint>> ByCode<T>(IReadOnlyList<T> rows, string list, Func<T, string?> pick)
        {
            List<string> codes = [.. rows.Select(pick).Where(code => code != null).Select(code => code!).Distinct()];
            IReadOnlyDictionary<string, string> labels = codes.Count == 0 ? new Dictionary<string, string>() : await lookups.GetLabelsAsync(list, codes, cancellationToken);
            return [.. rows.GroupBy(row => pick(row) ?? "Not recorded")
                .Select(group => new MetricPoint(labels.GetValueOrDefault(group.Key) ?? group.Key, group.Count()))
                .OrderByDescending(point => point.Value)];
        }

        return
        [
            new Metric("people.headcount", MetricAreas.People, "Headcount", active.Count, MetricUnit.Count, "Active and on notice",
                [.. active.GroupBy(row => row.DepartmentId is { } id ? departments.GetValueOrDefault(id) ?? "Unknown" : "No department").Select(group => new MetricPoint(group.Key, group.Count())).OrderByDescending(point => point.Value)],
                headcountTrend, Executive: true),
            new Metric("people.joiners", MetricAreas.People, "Joiners this month", people.Count(row => row.JoiningDate >= monthStart && row.JoiningDate <= today), MetricUnit.Count, $"{people.Count(row => row.JoiningDate > yearAgo && row.JoiningDate <= today)} in the last 12 months", Trend: joinTrend, Executive: true),
            new Metric("people.exits", MetricAreas.People, "Exits (12 months)", exits12, MetricUnit.Count, null, Trend: exitTrend, HigherIsBetter: false),
            new Metric("people.attrition", MetricAreas.People, "Attrition (12 months)", attrition, MetricUnit.Percent, "Exits over average headcount", Executive: true, HigherIsBetter: false),
            new Metric("people.tenure", MetricAreas.People, "Average tenure", tenure, MetricUnit.Score, "Years"),
            new Metric("people.notice", MetricAreas.People, "Serving notice", active.Count(row => row.Status == EmployeeStatus.OnNotice), MetricUnit.Count, HigherIsBetter: false),
            new Metric("people.probation", MetricAreas.People, "Probation ending (30 days)", active.Count(row => row.ConfirmationDate == null && row.ProbationEndDate is { } end && end >= today && end <= today.AddDays(30)), MetricUnit.Count, "Due for confirmation"),
            new Metric("people.gender", MetricAreas.People, "Gender", active.Count, MetricUnit.Count, null, await ByCode(active, LookupLists.Gender, row => row.GenderCode)),
            new Metric("people.employment-type", MetricAreas.People, "Employment type", active.Count, MetricUnit.Count, null, await ByCode(active, LookupLists.EmploymentType, row => row.EmploymentTypeCode)),
            new Metric("people.work-mode", MetricAreas.People, "Work mode", active.Count, MetricUnit.Count, null, await ByCode(active, LookupLists.WorkMode, row => row.WorkModeCode)),
        ];
    }
}

/// <summary>Employees in global search, within the caller's employee access.</summary>
internal sealed class EmployeeSearch(AppDbContext dbContext, EmployeeScope scope, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "employee";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Employee.View))
        {
            return [];
        }

        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);
        var rows = await visible
            .Where(row => row.DisplayName.Contains(term) || row.EmployeeCode.Contains(term) || (row.OfficialEmail != null && row.OfficialEmail.Contains(term)))
            .OrderBy(row => row.DisplayName)
            .Take(take * 2)
            .Select(row => new { row.Id, row.DisplayName, row.EmployeeCode, row.Designation, row.Status })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchHit(Kind, row.Id, row.DisplayName, $"{row.EmployeeCode}{(row.Designation is null ? string.Empty : " · " + row.Designation)}{(row.Status == EmployeeStatus.Exited ? " · exited" : string.Empty)}",
                $"/employees/{row.Id}", Math.Max(Score(row.DisplayName, term), Score(row.EmployeeCode, term)) + (row.Status == EmployeeStatus.Exited ? 0 : 0.05)))
            .OrderByDescending(hit => hit.Score).Take(take)];
    }

    private static double Score(string value, string term)
        => value.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1 : value.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.8 : value.Contains(" " + term, StringComparison.OrdinalIgnoreCase) ? 0.6 : 0.4;
}

/// <summary>Birthdays, work anniversaries, joinings, exits and probation ends on the HR calendar.</summary>
internal sealed class EmployeeCalendar(AppDbContext dbContext) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IQueryable<EmployeeEntity> query = dbContext.Employees.AsNoTracking();
        if (context.EmployeeScope is { } scope)
        {
            query = query.Where(row => scope.Contains(row.Id));
        }

        var people = await query
            .Select(row => new { row.Id, row.DisplayName, row.Status, row.DateOfBirth, row.JoiningDate, row.ExitDate, row.ProbationEndDate, row.ConfirmationDate })
            .ToListAsync(cancellationToken);
        List<CalendarEntry> entries = [];
        foreach (var person in people)
        {
            string link = $"/employees/{person.Id}";
            if (person.Status != EmployeeStatus.Exited)
            {
                if (person.DateOfBirth is { } born)
                {
                    foreach (DateOnly day in Occurrences(born, firstDay, lastDay))
                    {
                        entries.Add(new CalendarEntry(CalendarKinds.Birthday, $"{person.DisplayName}'s birthday", day, day, person.Id, link));
                    }
                }

                foreach (DateOnly day in Occurrences(person.JoiningDate, firstDay, lastDay).Where(day => day.Year > person.JoiningDate.Year))
                {
                    int years = day.Year - person.JoiningDate.Year;
                    entries.Add(new CalendarEntry(CalendarKinds.Anniversary, $"{person.DisplayName}: {years} year{(years == 1 ? string.Empty : "s")}", day, day, person.Id, link));
                }

                if (person.ConfirmationDate is null && person.ProbationEndDate is { } probation && probation >= firstDay && probation <= lastDay && !context.OnlyMine)
                {
                    entries.Add(new CalendarEntry(CalendarKinds.Review, $"{person.DisplayName}: probation ends", probation, probation, person.Id, link));
                }
            }

            if (person.JoiningDate >= firstDay && person.JoiningDate <= lastDay)
            {
                entries.Add(new CalendarEntry(CalendarKinds.Joining, $"{person.DisplayName} joins", person.JoiningDate, person.JoiningDate, person.Id, link));
            }

            if (person.ExitDate is { } exit && exit >= firstDay && exit <= lastDay)
            {
                entries.Add(new CalendarEntry(CalendarKinds.Exit, $"{person.DisplayName}'s last day", exit, exit, person.Id, link));
            }
        }

        return entries;
    }

    /// <summary>The yearly recurrences of a date within a range (29 February falls on 28 February in other years).</summary>
    public static IEnumerable<DateOnly> Occurrences(DateOnly date, DateOnly firstDay, DateOnly lastDay)
    {
        for (int year = firstDay.Year; year <= lastDay.Year; year++)
        {
            int day = date.Month == 2 && date.Day == 29 && !DateTime.IsLeapYear(year) ? 28 : date.Day;
            var occurrence = new DateOnly(year, date.Month, day);
            if (occurrence >= firstDay && occurrence <= lastDay)
            {
                yield return occurrence;
            }
        }
    }
}
