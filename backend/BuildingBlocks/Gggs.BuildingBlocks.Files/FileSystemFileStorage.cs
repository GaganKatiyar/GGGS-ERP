using System.Security.Cryptography;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Files;

/// <summary>Settings for the on-premise file share adapter.</summary>
public sealed class FileSystemStorageOptions
{
    public const string SectionName = "Gggs:Storage:FileSystem";

    /// <summary>Root folder or UNC share holding every tenant folder.</summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// On-premise document storage on a local folder or file share.
/// <para>
/// There is no signed-URL equivalent here, so downloads stream through the API. That is a little
/// more load on the service and one less way to leak a file: the permission check and the byte
/// stream stay in the same place.
/// </para>
/// </summary>
public sealed class FileSystemFileStorage(
    IOptions<FileSystemStorageOptions> options,
    ITenantContext tenantContext,
    ILogger<FileSystemFileStorage> logger) : IFileStorage
{
    private readonly string _rootPath = string.IsNullOrWhiteSpace(options.Value.RootPath)
        ? throw new InvalidOperationException(
            $"{FileSystemStorageOptions.SectionName}:RootPath must be set for the on-premise profile.")
        : Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredFile> SaveAsync(FileToStore file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        string storageKey = TenantStorageKey.Build(tenantContext, file.RelativeKey);
        string fullPath = ResolveFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (FileStream target = File.Create(fullPath))
        {
            await file.Content.CopyToAsync(target, cancellationToken);
        }

        await using FileStream written = File.OpenRead(fullPath);
        byte[] hash = await SHA256.HashDataAsync(written, cancellationToken);

        logger.LogInformation("Stored file {StorageKey} ({SizeBytes} bytes).", storageKey, written.Length);
        return new StoredFile(storageKey, written.Length, Convert.ToHexStringLower(hash));
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        string fullPath = ResolveFullPath(storageKey);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored file not found.", storageKey);
        }

        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(ResolveFullPath(storageKey)));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        string fullPath = ResolveFullPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>A file share cannot issue a signed link, so the caller streams the file instead.</summary>
    public Task<Uri?> TryCreateDownloadLinkAsync(
        string storageKey,
        TimeSpan validFor,
        string downloadFileName,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Uri?>(null);

    public Task PurgeTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        string tenantFolder = Path.Combine(_rootPath, TenantStorageKey.ContainerFor(tenantId));

        if (Directory.Exists(tenantFolder))
        {
            Directory.Delete(tenantFolder, recursive: true);
            logger.LogWarning("Purged all stored files for tenant {TenantId}.", tenantId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the key to an absolute path and then checks the result is still inside the root.
    /// The key is already sanitized; this catches anything that slipped through, such as a symlink
    /// pointing out of the share.
    /// </summary>
    private string ResolveFullPath(string storageKey)
    {
        (string container, string relativeKey) = TenantStorageKey.Resolve(tenantContext, storageKey);
        string candidate = Path.GetFullPath(Path.Combine(_rootPath, container, relativeKey));

        if (!candidate.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Resolved storage path escaped the configured root.");
        }

        return candidate;
    }
}
