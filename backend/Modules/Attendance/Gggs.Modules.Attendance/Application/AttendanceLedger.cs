using Gggs.Data;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Attendance.Application;

/// <summary>Absences from the judged days, for loss-of-pay in payroll.</summary>
internal sealed class AttendanceLedger(AppDbContext dbContext) : IAttendanceLedger
{
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
