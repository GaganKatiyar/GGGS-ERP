using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// What a biometric device's own push actually does, once <c>DeviceApiKeyAuthenticator</c> has
/// authenticated it and the request is running inside that device's tenant: resolve the device's
/// own user code to an employee via <see cref="DeviceUserMap"/> (a device speaks in device-local
/// codes, never this platform's employee ids), then record the punch through the same
/// <see cref="AttendancePunchService"/> every other punch source goes through - a device is just
/// another <see cref="PunchSource"/>, not a separate write path.
/// </summary>
public sealed class DeviceIngestService(
    IDeviceUserMapRepository deviceUserMaps, IBiometricDeviceRepository devices, AttendancePunchService punchService)
{
    public async Task<AttendancePunch> RecordPunchAsync(
        Guid deviceId, string deviceUserCode, DateTime punchTimeUtc, PunchDirection direction, string? externalRef,
        CancellationToken cancellationToken = default)
    {
        BiometricDevice device = await devices.GetAsync(deviceId, cancellationToken)
            ?? throw new NotFoundException(nameof(BiometricDevice), deviceId);

        Guard(device.Status == BiometricDeviceStatus.Active, "BIOMETRIC_DEVICE_NOT_ACTIVE", "This device is not active.");

        DeviceUserMap? map = await deviceUserMaps.GetAsync(deviceId, deviceUserCode, cancellationToken);
        Guard(map is not null, "DEVICE_USER_MAP_NOT_FOUND", $"Device user code '{deviceUserCode}' is not mapped to an employee.");

        device.RecordHeartbeat(punchTimeUtc);

        return await punchService.RecordAsync(
            map!.EmployeeId, punchTimeUtc, direction, PunchSource.Biometric, deviceId,
            latitude: null, longitude: null, ipAddress: null, externalRef, cancellationToken);
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
