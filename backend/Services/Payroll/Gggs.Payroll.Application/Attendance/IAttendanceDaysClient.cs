namespace Gggs.Payroll.Application.Attendance;

/// <summary>Mirrors Time's own <c>AttendanceDayStatus</c> by name - see
/// <see cref="IAttendanceDaysClient"/>'s own remarks on why this is a client-side copy rather than a
/// shared assembly reference.</summary>
public enum RemoteAttendanceDayStatus
{
    Present = 1,
    Absent = 2,
    HalfDay = 3,
    OnLeave = 4,
    WeeklyOff = 5,
    Holiday = 6,
    OnDuty = 7,
}

public sealed record RemoteAttendanceDay(DateOnly AttendanceDate, RemoteAttendanceDayStatus Status);

/// <summary>
/// Reads an employee's attendance days from the Time service for a payroll run's proration.
/// <para>
/// A direct synchronous call to another service's REST API rather than a local snapshot table fed
/// by events - the usual cross-service pattern in this codebase (see
/// <c>Gggs.Payroll.Domain.Snapshots.EmployeeSnapshot</c>) doesn't apply here because Time publishes no
/// integration events today. Acceptable because payroll calculation is an explicit, infrequent
/// admin/batch action, not a hot request path, so a synchronous read of already-settled attendance
/// data is a reasonable simplification for this v1 slice rather than new cross-service event
/// infrastructure in Time.
/// </para>
/// <para>
/// The caller's own bearer token is forwarded, so this only ever answers for data the acting user
/// already holds <c>attendance.record.view</c> for - the same per-request trust model
/// <c>HttpPermissionProvider</c> uses for Identity.
/// </para>
/// </summary>
public interface IAttendanceDaysClient
{
    Task<IReadOnlyList<RemoteAttendanceDay>> ListAsync(
        Guid employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
