using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Application.Attendance;

/// <summary>
/// Authenticates a biometric device's API key and sets the tenant context for the rest of the
/// request - the device-ingest equivalent of <c>SignInService.SignInWithPasswordAsync</c> plus the
/// <c>tenantSetter.SetTenant(...)</c> call <c>AuthenticationEndpoints.SignInAsync</c> makes right
/// after tenant discovery. The device-ingest endpoint is <c>[AllowAnonymous]</c> at the ASP.NET Core
/// level - <see cref="TenantContextMiddleware"/> only resolves a tenant from an already-authenticated
/// <c>context.User</c>, and a device is never that - so this is the one place in Time that sets the
/// tenant explicitly, the same deliberate exception <c>SignInAsync</c> already is.
/// </summary>
public sealed class DeviceApiKeyAuthenticator(
    IBiometricDeviceKeyRepository deviceKeys, IApiKeyGenerator apiKeys, ITenantContextSetter tenantSetter)
{
    /// <summary>Null when the key is missing, malformed or unknown - deliberately the same shape as
    /// a failed sign-in: the caller gets 401, with no distinction between "wrong key" and "no such
    /// device" that would let an attacker enumerate valid keys.</summary>
    public async Task<Guid?> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        string apiKeyHash = apiKeys.HashFor(apiKey);
        BiometricDeviceKey? key = await deviceKeys.GetByApiKeyHashAsync(apiKeyHash, cancellationToken);

        if (key is null)
        {
            return null;
        }

        // Set before any tenant-owned query: everything the device-ingest handler does next
        // (resolving the device, the user-code map, recording the punch) reads tenant-owned tables.
        tenantSetter.SetTenant(key.TenantId);

        return key.DeviceId;
    }
}
