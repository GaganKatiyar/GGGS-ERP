using System.Globalization;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Attendance.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Attendance.Application;

/// <summary>Attendance analytics over the judged days of the last 30 days.</summary>
internal sealed class AttendanceMetrics(AppDbContext dbContext) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Time];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateOnly since = context.Today.AddDays(-30);
        IQueryable<AttendanceDay> query = dbContext.AttendanceDays.AsNoTracking().Where(row => row.Date >= since && row.Date < context.Today);
        if (context.EmployeeScope is { } scope)
        {
            query = query.Where(row => scope.Contains(row.EmployeeId));
        }

        var days = await query.Select(row => new { row.Date, row.Status, row.LateMinutes, row.WorkedMinutes }).ToListAsync(cancellationToken);
        var working = days.Where(row => row.Status is AttendanceStatus.Present or AttendanceStatus.HalfDay or AttendanceStatus.Absent or AttendanceStatus.MissingPunch).ToList();
        var present = working.Where(row => row.Status is AttendanceStatus.Present or AttendanceStatus.HalfDay).ToList();
        return
        [
            new Metric("time.attendance-rate", MetricAreas.Time, "Attendance (30 days)", Rate(working.Select(row => row.Status)), MetricUnit.Percent, "Present over working days",
                Trend: [.. working.GroupBy(row => row.Date).OrderBy(group => group.Key).TakeLast(14).Select(group => new MetricPoint(group.Key.ToString("d MMM", CultureInfo.InvariantCulture), Rate(group.Select(row => row.Status))))],
                Executive: true),
            new Metric("time.late", MetricAreas.Time, "Late arrivals (30 days)", present.Count(row => row.LateMinutes > 0), MetricUnit.Count, null,
                [.. days.GroupBy(row => row.Status).Select(group => new MetricPoint(group.Key.ToString(), group.Count())).OrderByDescending(point => point.Value)], HigherIsBetter: false),
            new Metric("time.worked-hours", MetricAreas.Time, "Average hours worked", present.Count == 0 ? 0 : Math.Round((decimal)present.Average(row => row.WorkedMinutes) / 60m, 1), MetricUnit.Hours, "Per day present"),
        ];
    }

    /// <summary>Present counts as a day, a half day as half, absence and missing punches as none.</summary>
    public static decimal Rate(IEnumerable<AttendanceStatus> statuses)
    {
        List<AttendanceStatus> list = [.. statuses];
        return list.Count == 0 ? 0 : Math.Round(100m * list.Sum(status => status == AttendanceStatus.Present ? 1m : status == AttendanceStatus.HalfDay ? 0.5m : 0m) / list.Count, 1);
    }
}

/// <summary>Biometric and access-control punches through the import framework, as the device import endpoint takes them.</summary>
internal sealed class PunchImportHandler(AttendanceService attendance) : IImportHandler
{
    public string Key => "attendance-punches";

    public string Title => "Biometric punches";

    public string Description => "Punches exported from biometric or access-control devices; each day is judged again after import.";

    public string Permission => Permissions.Attendance.DeviceManage;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new("device", "Registered device code", true, "BLR-GATE-1"),
        new("employeeCode", "Employee code", true, "EMP00001"),
        new("punchedAt", "When (ISO 8601 with offset)", true, "2026-10-19T09:02:00+05:30"),
    ];

    public async Task<ImportOutcome> RunAsync(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<ImportRowResult> results = [];
        List<(string Device, DevicePunch Punch)> valid = [];
        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, string> row = rows[index];
            List<string> errors = [];
            string device = row.GetValueOrDefault("device") ?? string.Empty;
            string code = row.GetValueOrDefault("employeeCode") ?? string.Empty;
            if (device.Length == 0)
            {
                errors.Add("Give the device code.");
            }

            if (code.Length == 0)
            {
                errors.Add("Give the employee code.");
            }

            if (!DateTimeOffset.TryParse(row.GetValueOrDefault("punchedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at))
            {
                errors.Add("The time must be ISO 8601, e.g. 2026-10-19T09:02:00+05:30.");
            }

            if (errors.Count == 0)
            {
                valid.Add((device, new DevicePunch(code, at)));
            }

            results.Add(new ImportRowResult(index + 2, errors, errors.Count == 0 ? $"{code} at {at:yyyy-MM-dd HH:mm}" : null));
        }

        int imported = 0;
        if (commit)
        {
            foreach (IGrouping<string, (string Device, DevicePunch Punch)> group in valid.GroupBy(item => item.Device, StringComparer.OrdinalIgnoreCase))
            {
                PunchImportResult result = await attendance.ImportAsync(new PunchImport(group.Key, [.. group.Select(item => item.Punch)]), cancellationToken);
                imported += result.Imported;
                if (result.UnknownEmployeeCodes.Count > 0)
                {
                    results.Add(new ImportRowResult(0, [$"Unknown employee codes skipped: {string.Join(", ", result.UnknownEmployeeCodes)}."]));
                }
            }
        }

        return new ImportOutcome(rows.Count, valid.Count, imported, results);
    }
}
