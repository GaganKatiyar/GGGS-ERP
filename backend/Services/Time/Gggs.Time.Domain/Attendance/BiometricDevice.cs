using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Attendance;

public enum BiometricDeviceStatus
{
    Active = 1,
    Inactive = 2,
}

/// <summary>
/// A registered attendance-punch device - a fingerprint/face terminal at a branch, pushing punches
/// directly to <c>/api/v1/attendance/device-ingest</c> rather than through a user's own session (see
/// architecture.md's "direct device push" integration path, distinct from the heavier on-prem
/// <c>Gggs.AttendanceConnector</c> agent it also describes, which is out of scope here). No
/// <c>ConnectorId</c> in this v1 slice - a device authenticates with its own API key, not through an
/// intermediary agent. The key itself is never stored: <see cref="ApiKeyHash"/> is an HMAC of it, the
/// same blind-index shape architecture.md uses for other lookup-by-secret fields, so a stolen backup
/// cannot be replayed as a working key.
/// </summary>
public sealed class BiometricDevice : TenantAggregateRoot
{
    private BiometricDevice()
    {
        SerialNumber = null!;
        Name = null!;
        ApiKeyHash = null!;
    }

    public string SerialNumber { get; private set; }

    public string Name { get; private set; }

    public Guid BranchId { get; private set; }

    public string ApiKeyHash { get; private set; }

    public BiometricDeviceStatus Status { get; private set; }

    public DateTime? LastSeenDate { get; private set; }

    public static BiometricDevice Register(string serialNumber, string name, Guid branchId, string apiKeyHash) => new()
    {
        SerialNumber = Guard.MaxLength(Guard.NotNullOrWhiteSpace(serialNumber).Trim(), 100),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        BranchId = Guard.NotEmpty(branchId),
        ApiKeyHash = Guard.NotNullOrWhiteSpace(apiKeyHash),
        Status = BiometricDeviceStatus.Active,
        IsActive = true,
    };

    public void RecordHeartbeat(DateTime seenAtUtc) => LastSeenDate = seenAtUtc;

    public void RotateApiKey(string apiKeyHash) => ApiKeyHash = Guard.NotNullOrWhiteSpace(apiKeyHash);

    public void Deactivate() => Status = BiometricDeviceStatus.Inactive;

    public void Reactivate() => Status = BiometricDeviceStatus.Active;
}

/// <summary>
/// Maps a device's own internal user identifier (its fingerprint template's enrolled user code, not
/// this platform's <c>Guid</c>) to the employee it belongs to - a device speaks in device-local codes,
/// so every punch it pushes needs this lookup before it can become an <c>AttendancePunch.EmployeeId</c>.
/// </summary>
public sealed class DeviceUserMap : TenantAggregateRoot
{
    private DeviceUserMap()
    {
        DeviceUserCode = null!;
    }

    public Guid DeviceId { get; private set; }

    public string DeviceUserCode { get; private set; }

    public Guid EmployeeId { get; private set; }

    public static DeviceUserMap Create(Guid deviceId, string deviceUserCode, Guid employeeId) => new()
    {
        DeviceId = Guard.NotEmpty(deviceId),
        DeviceUserCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(deviceUserCode).Trim(), 50),
        EmployeeId = Guard.NotEmpty(employeeId),
        IsActive = true,
    };
}

/// <summary>
/// A device's API key hash, kept in a table separate from <see cref="BiometricDevice"/> itself and
/// deliberately <b>not</b> <c>ITenantOwned</c> - the same shape and the same reason as
/// <c>Gggs.BuildingBlocks.Messaging.OutboxMessage</c> (see its own remarks): the device-ingest
/// endpoint authenticates the key before any tenant context exists for the request, so this lookup
/// must run unfiltered by design rather than through a query the tenant filter or row-level security
/// would silently empty out. It carries <see cref="TenantId"/> as a plain column purely so the
/// authentication handler can hand that value to <c>ITenantContextSetter.SetTenant</c> once the key
/// is verified - mirroring exactly how Identity's own sign-in flow resolves and sets the tenant
/// before ever touching a tenant-owned table. <see cref="BiometricDeviceService"/> keeps this row in
/// lockstep with <see cref="BiometricDevice.ApiKeyHash"/> on every register/rotate, so the two never
/// drift: this table is a lookup index for that one, not a second source of truth.
/// </summary>
public sealed class BiometricDeviceKey
{
    private BiometricDeviceKey()
    {
        ApiKeyHash = null!;
    }

    public Guid DeviceId { get; private set; }

    public Guid TenantId { get; private set; }

    public string ApiKeyHash { get; private set; }

    public static BiometricDeviceKey Create(Guid deviceId, Guid tenantId, string apiKeyHash) => new()
    {
        DeviceId = Guard.NotEmpty(deviceId),
        TenantId = Guard.NotEmpty(tenantId),
        ApiKeyHash = Guard.NotNullOrWhiteSpace(apiKeyHash),
    };

    public void RotateApiKey(string apiKeyHash) => ApiKeyHash = Guard.NotNullOrWhiteSpace(apiKeyHash);
}
