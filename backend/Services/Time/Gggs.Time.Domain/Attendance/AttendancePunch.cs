using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum PunchDirection
{
    In = 1,
    Out = 2,

    /// <summary>The source could not say in/out (a device that only reports "seen") - day
    /// processing sorts a day's punches by time and infers direction by position instead.</summary>
    Auto = 3,
}

public enum PunchSource
{
    Web = 1,
    Mobile = 2,
    Biometric = 3,
    Import = 4,
    Manual = 5,
}

/// <summary>
/// One raw check-in/out event. Append-only by convention (nothing in this service ever updates or
/// soft-deletes a punch): the architecture's audit and dispute-resolution story for attendance
/// depends on the raw record surviving unchanged even after <see cref="AttendanceDay"/> is
/// recomputed from it. Corrections go through <see cref="AttendanceRegularization"/> instead of
/// editing history.
/// </summary>
public sealed class AttendancePunch : TenantAggregateRoot
{
    private AttendancePunch()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateTime PunchTimeUtc { get; private set; }

    public PunchDirection Direction { get; private set; }

    public PunchSource Source { get; private set; }

    public Guid? DeviceId { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public string? IpAddress { get; private set; }

    /// <summary>The source system's own id for this punch (a biometric device's log sequence
    /// number, an import row key) - the idempotency key a redelivered/re-imported punch is
    /// deduplicated on, together with <see cref="Source"/>. Null for punches with no natural key of
    /// their own (Web/Mobile), which cannot collide the same way.</summary>
    public string? ExternalRef { get; private set; }

    /// <summary>False for a punch day-processing decided not to trust (e.g. a geo-fence violation)
    /// - kept rather than rejected outright, so the record of "something was punched" is never
    /// lost, but excluded from <see cref="AttendanceDay"/> computation.</summary>
    public bool IsValid { get; private set; }

    public static AttendancePunch Record(
        Guid employeeId,
        DateTime punchTimeUtc,
        PunchDirection direction,
        PunchSource source,
        Guid? deviceId,
        decimal? latitude,
        decimal? longitude,
        string? ipAddress,
        string? externalRef,
        bool isValid) => new()
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            PunchTimeUtc = punchTimeUtc,
            Direction = direction,
            Source = source,
            DeviceId = deviceId,
            Latitude = latitude,
            Longitude = longitude,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : Guard.MaxLength(ipAddress, 45),
            ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? null : Guard.MaxLength(externalRef, 100),
            IsValid = isValid,
            IsActive = true,
        };
}
