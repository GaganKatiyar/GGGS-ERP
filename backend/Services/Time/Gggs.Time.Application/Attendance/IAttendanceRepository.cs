using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Application.Attendance;

public interface IShiftRepository
{
    Task<Shift?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Shift?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    void Add(Shift shift);
}

public interface IShiftRosterRepository
{
    Task<ShiftRoster?> GetForDateAsync(Guid employeeId, DateOnly rosterDate, CancellationToken cancellationToken = default);

    void Add(ShiftRoster roster);
}

public interface IAttendancePunchRepository
{
    void Add(AttendancePunch punch);

    Task<bool> ExternalRefExistsAsync(PunchSource source, string externalRef, CancellationToken cancellationToken = default);

    /// <summary>Every valid punch recorded for one employee on one calendar date, in the tenant's
    /// own time zone - what <see cref="AttendanceDayProcessor"/> recomputes an
    /// <see cref="AttendanceDay"/> from. Bounded by a UTC window the caller computes from the
    /// employee's date and time zone, since <see cref="AttendancePunch.PunchTimeUtc"/> is stored in
    /// UTC but a "day" is a local-calendar concept.</summary>
    Task<IReadOnlyList<AttendancePunch>> GetForDateAsync(
        Guid employeeId, DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken = default);
}

public interface IAttendanceDayRepository
{
    Task<AttendanceDay?> GetAsync(Guid employeeId, DateOnly attendanceDate, CancellationToken cancellationToken = default);

    void Add(AttendanceDay day);
}

public interface IAttendanceRegularizationRepository
{
    Task<AttendanceRegularization?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(AttendanceRegularization regularization);
}

public interface IOvertimeRequestRepository
{
    Task<OvertimeRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(OvertimeRequest request);
}

public interface IBiometricDeviceRepository
{
    Task<BiometricDevice?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SerialNumberExistsAsync(string serialNumber, CancellationToken cancellationToken = default);

    void Add(BiometricDevice device);
}

public interface IDeviceUserMapRepository
{
    Task<DeviceUserMap?> GetAsync(Guid deviceId, string deviceUserCode, CancellationToken cancellationToken = default);

    void Add(DeviceUserMap map);
}

/// <summary>Deliberately separate from <see cref="IBiometricDeviceRepository"/>: this is the one
/// lookup in Time that must work with no tenant context, see <c>BiometricDeviceKey</c>'s own remarks.</summary>
public interface IBiometricDeviceKeyRepository
{
    Task<BiometricDeviceKey?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default);

    Task<BiometricDeviceKey?> GetAsync(Guid deviceId, CancellationToken cancellationToken = default);

    void Add(BiometricDeviceKey key);
}
