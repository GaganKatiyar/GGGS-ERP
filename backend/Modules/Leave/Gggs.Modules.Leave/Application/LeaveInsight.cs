using System.Globalization;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

/// <summary>Leave analytics: who is away, what is waiting, and what has been taken.</summary>
internal sealed class LeaveMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Time];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateOnly today = context.Today;
        var yearStart = new DateOnly(today.Year, 1, 1);
        IQueryable<LeaveRequest> requests = dbContext.LeaveRequests.AsNoTracking().Where(row => row.EndDate >= yearStart || row.Status == LeaveRequestStatus.Pending);
        if (context.EmployeeScope is { } scope)
        {
            requests = requests.Where(row => scope.Contains(row.EmployeeId));
        }

        List<LeaveRequest> rows = await requests.ToListAsync(cancellationToken);
        Dictionary<Guid, string> types = await dbContext.LeaveTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        List<LeaveRequest> approved = [.. rows.Where(row => row.Status == LeaveRequestStatus.Approved)];
        return
        [
            new Metric("time.on-leave", MetricAreas.Time, "On leave today", approved.Count(row => row.StartDate <= today && row.EndDate >= today), MetricUnit.Count,
                $"{approved.Count(row => row.StartDate > today && row.StartDate <= today.AddDays(7))} starting in the next 7 days", Executive: true, HigherIsBetter: false),
            new Metric("time.leave-pending", MetricAreas.Time, "Leave awaiting approval", rows.Count(row => row.Status == LeaveRequestStatus.Pending), MetricUnit.Count, HigherIsBetter: false),
            new Metric("time.leave-taken", MetricAreas.Time, "Leave taken this year", approved.Where(row => row.StartDate <= today).Sum(row => row.Days), MetricUnit.Days, null,
                [.. approved.Where(row => row.StartDate <= today).GroupBy(row => types.GetValueOrDefault(row.LeaveTypeId) ?? "Other").Select(group => new MetricPoint(group.Key, group.Sum(row => row.Days))).OrderByDescending(point => point.Value)],
                [.. Enumerable.Range(1, today.Month).Select(month => new MetricPoint(new DateOnly(today.Year, month, 1).ToString("MMM", CultureInfo.InvariantCulture), approved.Where(row => row.StartDate.Year == today.Year && row.StartDate.Month == month && row.StartDate <= today).Sum(row => row.Days)))]),
        ];
    }
}

/// <summary>Holidays and leave on the HR calendar.</summary>
internal sealed class LeaveCalendarEntries(AppDbContext dbContext, IEmployeeDirectory employees) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> EntriesAsync(DateOnly firstDay, DateOnly lastDay, CalendarContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<HolidayCalendar> calendars = await dbContext.HolidayCalendars.AsNoTracking().ToListAsync(cancellationToken);
        List<Holiday> holidays = await dbContext.Holidays.AsNoTracking().Where(row => row.Date >= firstDay && row.Date <= lastDay).ToListAsync(cancellationToken);
        HashSet<Guid>? mine = null;
        if (context.OnlyMine && context.EmployeeId is { } me && await employees.GetAsync(me, cancellationToken) is { } person)
        {
            HolidayCalendar? own = LeaveCalendarService.CalendarFor(calendars, person.LocationId);
            mine = own is null ? [] : [own.Id];
        }

        Dictionary<Guid, string> calendarNames = calendars.ToDictionary(row => row.Id, row => row.Name);
        List<CalendarEntry> entries = [.. holidays
            .Where(row => mine is null || mine.Contains(row.CalendarId))
            .Select(row => new CalendarEntry(CalendarKinds.Holiday, row.Name + (row.IsOptional ? " (optional)" : string.Empty), row.Date, row.Date, null, null, calendars.Count > 1 ? calendarNames.GetValueOrDefault(row.CalendarId) : null))];

        IQueryable<LeaveRequest> leave = dbContext.LeaveRequests.AsNoTracking()
            .Where(row => row.StartDate <= lastDay && row.EndDate >= firstDay && (row.Status == LeaveRequestStatus.Approved || row.Status == LeaveRequestStatus.Pending));
        if (context.EmployeeScope is { } scope)
        {
            leave = leave.Where(row => scope.Contains(row.EmployeeId));
        }

        List<LeaveRequest> requests = await leave.Take(2000).ToListAsync(cancellationToken);
        Dictionary<Guid, string> types = await dbContext.LeaveTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(requests.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(row => row.Id, row => row.DisplayName);
        entries.AddRange(requests.Select(row => new CalendarEntry(
            CalendarKinds.Leave,
            $"{names.GetValueOrDefault(row.EmployeeId) ?? "Someone"}: {types.GetValueOrDefault(row.LeaveTypeId) ?? "Leave"}{(row.Status == LeaveRequestStatus.Pending ? " (pending)" : string.Empty)}",
            row.StartDate,
            row.EndDate,
            row.EmployeeId,
            context.OnlyMine ? "/me/leave" : "/leave/requests",
            $"{row.Days.ToString("0.#", CultureInfo.InvariantCulture)} day(s)")));
        return entries;
    }
}

/// <summary>Bulk import of holidays into holiday calendars.</summary>
internal sealed class HolidayImport(AppDbContext dbContext) : IImportHandler
{
    public string Key => "holidays";

    public string Title => "Holidays";

    public string Description => "Public and optional holidays, by holiday calendar.";

    public string Permission => Permissions.Leave.HolidayManage;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new("calendar", "Holiday calendar code", true, "GENERAL"),
        new("date", "Date (yyyy-mm-dd)", true, "2027-01-26"),
        new("name", "Holiday name", true, "Republic Day"),
        new("optional", "Optional holiday (yes/no)", false, "no"),
    ];

    public async Task<ImportOutcome> RunAsync(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Dictionary<string, Guid> calendars = await dbContext.HolidayCalendars.AsNoTracking().ToDictionaryAsync(row => row.Code, row => row.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        HashSet<(Guid, DateOnly)> existing = [.. (await dbContext.Holidays.AsNoTracking().Select(row => new { row.CalendarId, row.Date }).ToListAsync(cancellationToken)).Select(row => (row.CalendarId, row.Date))];
        List<ImportRowResult> results = [];
        List<Holiday> valid = [];
        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, string> row = rows[index];
            List<string> errors = [];
            if (!calendars.TryGetValue(row.GetValueOrDefault("calendar") ?? string.Empty, out Guid calendarId))
            {
                errors.Add($"No holiday calendar has the code '{row.GetValueOrDefault("calendar")}'.");
            }

            if (!DateOnly.TryParseExact(row.GetValueOrDefault("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                errors.Add("The date must be yyyy-mm-dd.");
            }

            string name = row.GetValueOrDefault("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            {
                errors.Add("Give a name of up to 100 characters.");
            }

            if (errors.Count == 0 && !existing.Add((calendarId, date)))
            {
                errors.Add("That calendar already has a holiday on this date.");
            }

            if (errors.Count == 0)
            {
                bool optional = (row.GetValueOrDefault("optional") ?? string.Empty).Trim().ToUpperInvariant() is "YES" or "Y" or "TRUE" or "1";
                valid.Add(Holiday.Create(calendarId, date, name.Trim(), optional));
            }

            results.Add(new ImportRowResult(index + 2, errors, errors.Count == 0 ? $"{name.Trim()} on {date:d MMM yyyy}" : null));
        }

        if (commit && valid.Count > 0)
        {
            dbContext.Holidays.AddRange(valid);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ImportOutcome(rows.Count, valid.Count, commit ? valid.Count : 0, results);
    }
}
