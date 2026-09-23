using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Secrets;

/// <summary>Settings for the on-premise secret store.</summary>
public sealed class FileSecretStoreOptions
{
    public const string SectionName = "Gggs:Secrets:File";

    /// <summary>Path to the encrypted secret file. Back it up with the database; losing it loses the secrets.</summary>
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// On-premise secret store: a single encrypted file, protected with ASP.NET Core Data Protection.
/// <para>
/// Customers running this themselves rarely have a vault, and a homegrown cipher would be worse
/// than either option. Data Protection is used so the key material is managed by the framework -
/// DPAPI-backed on Windows, key files on Linux - and the file is useless if copied off the host.
/// Customers who do run HashiCorp Vault or similar get an adapter instead of this one.
/// </para>
/// </summary>
public sealed class ProtectedFileSecretStore : ISecretStore
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly string _filePath;
    private readonly IDataProtector _protector;
    private readonly ILogger<ProtectedFileSecretStore> _logger;

    public ProtectedFileSecretStore(
        IOptions<FileSecretStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ProtectedFileSecretStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _filePath = string.IsNullOrWhiteSpace(options.Value.FilePath)
            ? throw new InvalidOperationException($"{FileSecretStoreOptions.SectionName}:FilePath must be set.")
            : Path.GetFullPath(options.Value.FilePath);

        _protector = dataProtectionProvider.CreateProtector("Gggs.Secrets.v1");
        _logger = logger;
    }

    public async Task<string> GetAsync(string secretName, CancellationToken cancellationToken = default)
        => await TryGetAsync(secretName, cancellationToken) ?? throw new SecretNotFoundException(secretName);

    public async Task<string?> TryGetAsync(string secretName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        Dictionary<string, string> secrets = await ReadAllAsync(cancellationToken);

        return secrets.TryGetValue(secretName, out string? protectedValue)
            ? _protector.Unprotect(protectedValue)
            : null;
    }

    public async Task SetAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, string> secrets = await ReadAllUnsafeAsync(cancellationToken);
            secrets[secretName] = _protector.Protect(secretValue);
            await WriteAllUnsafeAsync(secrets, cancellationToken);
            _logger.LogInformation("Secret {SecretName} was written to the local store.", secretName);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task DeleteAsync(string secretName, CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, string> secrets = await ReadAllUnsafeAsync(cancellationToken);
            if (secrets.Remove(secretName))
            {
                await WriteAllUnsafeAsync(secrets, cancellationToken);
                _logger.LogWarning("Secret {SecretName} was deleted from the local store.", secretName);
            }
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadAllUnsafeAsync(cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAllUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using FileStream stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes to a temporary file and then replaces, so a crash mid-write cannot leave the store
    /// truncated - which would lock every tenant out of their own database.
    /// </summary>
    private async Task WriteAllUnsafeAsync(Dictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        string temporaryPath = _filePath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, secrets, cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
