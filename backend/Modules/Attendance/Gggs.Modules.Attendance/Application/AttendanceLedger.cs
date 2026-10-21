using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Attendance.Application;

/// <summary>Absences from the judged days, for loss-of-pay in payroll and absconding checks in separation.</summary>
internal sealed class AttendanceLedger(AppDbContext dbContext, AttendanceService attendance, ITenantClock clock) : IAttendanceLedger
{
    public async Task<IReadOnlyList<AbsenceStreak>> AbsenceStreaksAsync(IEnumerable<Guid> employeeIds, int minimumDays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        DateOnly yesterday = (await clock.TodayAsync(cancellationToken)).AddDays(-1);
        DateOnly from = yesterday.AddDays(-Math.Max(30, minimumDays * 3));
        List<AbsenceStreak> streaks = [];
        foreach (Guid[] chunk in employeeIds.Distinct().Chunk(200))
        {
            IReadOnlyList<DayEvaluation> days = await attendance.EvaluateAsync(chunk, from, yesterday, cancellationToken);
            foreach (IGrouping<Guid, DayEvaluation> person in days.GroupBy(day => day.EmployeeId))
            {
                int absent = 0;
                DateOnly? firstAbsent = null;
                DateOnly? lastWorked = null;
                foreach (DayEvaluation day in person.OrderByDescending(day => day.Date))
                {
                    if (day.Status is AttendanceStatus.WeeklyOff or AttendanceStatus.Holiday)
                    {
                        continue;
                    }

                    if (day.Status != AttendanceStatus.Absent)
                    {
                        lastWorked = day.Status == AttendanceStatus.OnLeave ? null : day.Date;
                        break;
                    }

                    absent++;
                    firstAbsent = day.Date;
                }

                if (absent >= minimumDays && lastWorked is { } worked && firstAbsent is { } first)
                {
                    streaks.Add(new AbsenceStreak(person.Key, first, worked, absent));
                }
            }
        }

        return streaks;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> AbsenceDaysAsync(IEnumerable<Guid> employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var rows = await dbContext.AttendanceDays.AsNoTracking()
            .Where(day => ids.Contains(day.EmployeeId) && day.Date >= firstDay && day.Date <= lastDay
                && (day.Status == AttendanceStatus.Absent || day.Status == AttendanceStatus.HalfDay))
            .GroupBy(day => day.EmployeeId)
            .Select(group => new
            {
                EmployeeId = group.Key,
                Absent = group.Count(day => day.Status == AttendanceStatus.Absent),
                Half = group.Count(day => day.Status == AttendanceStatus.HalfDay),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.EmployeeId, row => row.Absent + (row.Half * 0.5m));
    }
}
