using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Snapshots;

namespace Gggs.Time.Application.Attendance;

public sealed class AttendancePunchService(
    IAttendancePunchRepository punches,
    AttendanceDayProcessor dayProcessor,
    IEmployeeSnapshotRepository snapshots,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<AttendancePunch> RecordAsync(
        Guid? employeeId,
        DateTime punchTimeUtc,
        PunchDirection direction,
        PunchSource source,
        Guid? deviceId,
        decimal? latitude,
        decimal? longitude,
        string? ipAddress,
        string? externalRef,
        CancellationToken cancellationToken = default)
    {
        Guid resolvedEmployeeId = employeeId ?? currentUser.EmployeeId
            ?? throw new ForbiddenException("ATTENDANCE_NO_EMPLOYEE_PROFILE", "Your account has no linked employee profile.");

        EmployeeSnapshot? employee = await snapshots.GetAsync(resolvedEmployeeId, cancellationToken);
        Guard(employee is not null, "ATTENDANCE_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "ATTENDANCE_EMPLOYEE_EXITED", "This employee has already exited.");

        if (!string.IsNullOrWhiteSpace(externalRef))
        {
            bool duplicate = await punches.ExternalRefExistsAsync(source, externalRef, cancellationToken);
            Guard(!duplicate, "ATTENDANCE_PUNCH_DUPLICATE", "A punch with this external reference has already been recorded.");
        }

        AttendancePunch punch = AttendancePunch.Record(
            resolvedEmployeeId, punchTimeUtc, direction, source, deviceId, latitude, longitude, ipAddress, externalRef, isValid: true);

        punches.Add(punch);

        DateOnly attendanceDate = DateOnly.FromDateTime(punchTimeUtc);
        await dayProcessor.RecomputeAsync(resolvedEmployeeId, attendanceDate, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return punch;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
