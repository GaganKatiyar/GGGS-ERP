using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Secrets;

/// <summary>
/// Cloud secret store: Azure Key Vault, reached with the service's managed identity.
/// <para>
/// Values are cached briefly. Key Vault is rate-limited per vault, and a tenant database connection
/// string is fetched on essentially every request; a short cache keeps that from becoming the
/// platform's first bottleneck. The window is deliberately small so a rotation takes effect quickly.
/// </para>
/// </summary>
public sealed class KeyVaultSecretStore(
    SecretClient secretClient,
    IMemoryCache cache,
    ILogger<KeyVaultSecretStore> logger) : ISecretStore
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string> GetAsync(string secretName, CancellationToken cancellationToken = default)
        => await TryGetAsync(secretName, cancellationToken) ?? throw new SecretNotFoundException(secretName);

    public async Task<string?> TryGetAsync(string secretName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        if (cache.TryGetValue(CacheKey(secretName), out string? cached))
        {
            return cached;
        }

        try
        {
            Response<KeyVaultSecret> response = await secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            string value = response.Value.Value;

            cache.Set(CacheKey(secretName), value, CacheDuration);
            return value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Never log the name at a level that pages someone: a missing optional secret is normal.
            logger.LogDebug("Secret {SecretName} does not exist in the vault.", secretName);
            return null;
        }
    }

    public async Task SetAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        await secretClient.SetSecretAsync(secretName, secretValue, cancellationToken);
        cache.Remove(CacheKey(secretName));
        logger.LogInformation("Secret {SecretName} was written.", secretName);
    }

    public async Task DeleteAsync(string secretName, CancellationToken cancellationToken = default)
    {
        await secretClient.StartDeleteSecretAsync(secretName, cancellationToken);
        cache.Remove(CacheKey(secretName));
        logger.LogWarning("Secret {SecretName} was deleted.", secretName);
    }

    private static string CacheKey(string secretName) => $"gggs:secret:{secretName}";
}
