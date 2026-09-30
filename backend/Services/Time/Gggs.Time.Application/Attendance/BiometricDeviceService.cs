using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Registering and administering biometric devices - ordinary tenant-scoped admin operations
/// (an HR/IT admin acting through their own authenticated session), unlike the device's own
/// device-ingest calls which authenticate as the device itself. See
/// <see cref="Domain.Attendance.BiometricDevice"/>'s own remarks on why the API key is never stored,
/// only its hash - which is why <see cref="RegisterAsync"/> and <see cref="RotateApiKeyAsync"/> are
/// the only two places the plaintext key is ever visible, and only in their own return value. Every
/// register/rotate also writes the matching <see cref="BiometricDeviceKey"/> row in the same
/// transaction, since that is the only copy the device-ingest handler can actually read (see its own
/// remarks on why it is not <c>ITenantOwned</c>).
/// </summary>
public sealed class BiometricDeviceService(
    IBiometricDeviceRepository devices,
    IBiometricDeviceKeyRepository deviceKeys,
    IApiKeyGenerator apiKeys,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
{
    public async Task<(BiometricDevice Device, string ApiKey)> RegisterAsync(
        string serialNumber, string name, Guid branchId, CancellationToken cancellationToken = default)
    {
        Guard(
            !await devices.SerialNumberExistsAsync(serialNumber, cancellationToken),
            "BIOMETRIC_DEVICE_SERIAL_ALREADY_EXISTS",
            $"A device with serial number '{serialNumber}' is already registered.");

        (string apiKey, string apiKeyHash) = apiKeys.Generate();
        BiometricDevice device = BiometricDevice.Register(serialNumber, name, branchId, apiKeyHash);
        devices.Add(device);
        deviceKeys.Add(BiometricDeviceKey.Create(device.Id, tenantContext.TenantId, apiKeyHash));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (device, apiKey);
    }

    public async Task<(BiometricDevice Device, string ApiKey)> RotateApiKeyAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        BiometricDevice device = await GetOrThrowAsync(deviceId, cancellationToken);

        BiometricDeviceKey key = await deviceKeys.GetAsync(deviceId, cancellationToken)
            ?? throw new NotFoundException(nameof(BiometricDeviceKey), deviceId);

        (string apiKey, string apiKeyHash) = apiKeys.Generate();
        device.RotateApiKey(apiKeyHash);
        key.RotateApiKey(apiKeyHash);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (device, apiKey);
    }

    public async Task<BiometricDevice> DeactivateAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        BiometricDevice device = await GetOrThrowAsync(deviceId, cancellationToken);
        device.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return device;
    }

    public async Task<BiometricDevice> ReactivateAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        BiometricDevice device = await GetOrThrowAsync(deviceId, cancellationToken);
        device.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return device;
    }

    private async Task<BiometricDevice> GetOrThrowAsync(Guid deviceId, CancellationToken cancellationToken)
        => await devices.GetAsync(deviceId, cancellationToken)
            ?? throw new NotFoundException(nameof(BiometricDevice), deviceId);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}

/// <summary>Mapping a device's own internal user code to the employee it belongs to - ordinary
/// tenant-scoped admin, same as device registration.</summary>
public sealed class DeviceUserMapService(IDeviceUserMapRepository maps, IUnitOfWork unitOfWork)
{
    public async Task<DeviceUserMap> MapAsync(
        Guid deviceId, string deviceUserCode, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Guard(
            await maps.GetAsync(deviceId, deviceUserCode, cancellationToken) is null,
            "DEVICE_USER_MAP_ALREADY_EXISTS",
            $"Device user code '{deviceUserCode}' is already mapped for this device.");

        DeviceUserMap map = DeviceUserMap.Create(deviceId, deviceUserCode, employeeId);
        maps.Add(map);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return map;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
