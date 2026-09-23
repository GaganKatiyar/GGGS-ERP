using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Files;

/// <summary>
/// Cloud document storage on Azure Blob Storage, one container per tenant.
/// <para>
/// A container per tenant rather than a shared container with prefixes: it makes a tenant's data a
/// single object to delete at offboarding, to lifecycle-manage, and to wrap in a customer-managed
/// encryption scope for the dedicated tier.
/// </para>
/// </summary>
public sealed class AzureBlobFileStorage(
    BlobServiceClient serviceClient,
    ITenantContext tenantContext,
    ILogger<AzureBlobFileStorage> logger) : IFileStorage
{
    public async Task<StoredFile> SaveAsync(FileToStore file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        string storageKey = TenantStorageKey.Build(tenantContext, file.RelativeKey);
        (string container, string relativeKey) = TenantStorageKey.Resolve(tenantContext, storageKey);

        BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(container);

        // Private by default. A public container would make every URL a data breach.
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        BlobClient blob = containerClient.GetBlobClient(relativeKey);

        BlobUploadOptions uploadOptions = new()
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
            Metadata = file.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            // Refuse to silently overwrite: new versions get their own key.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };

        // Hash while uploading rather than reading the blob back afterwards. The CryptoStream is
        // disposed before the hash is read, so the final block has been processed.
        using SHA256 hasher = SHA256.Create();
        await using (var hashing = new CryptoStream(file.Content, hasher, CryptoStreamMode.Read, leaveOpen: true))
        {
            await blob.UploadAsync(hashing, uploadOptions, cancellationToken);
        }

        BlobProperties properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);

        logger.LogInformation("Stored blob {StorageKey} ({SizeBytes} bytes).", storageKey, properties.ContentLength);
        return new StoredFile(storageKey, properties.ContentLength, Convert.ToHexStringLower(hasher.Hash!));
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        => await GetBlobClient(storageKey).OpenReadAsync(cancellationToken: cancellationToken);

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        => await GetBlobClient(storageKey).ExistsAsync(cancellationToken);

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        => await GetBlobClient(storageKey).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);

    /// <summary>
    /// A read-only SAS for one blob, valid for minutes, forcing an attachment download with the
    /// original file name. Scoped to a single blob rather than the container, so a leaked link
    /// exposes one document and not a tenant's entire archive.
    /// </summary>
    public Task<Uri?> TryCreateDownloadLinkAsync(
        string storageKey,
        TimeSpan validFor,
        string downloadFileName,
        CancellationToken cancellationToken = default)
    {
        BlobClient blob = GetBlobClient(storageKey);

        if (!blob.CanGenerateSasUri)
        {
            // Managed identity without a user delegation key: stream through the API instead.
            return Task.FromResult<Uri?>(null);
        }

        BlobSasBuilder sas = new(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(validFor))
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            // Small backdate to tolerate clock skew between our host and storage.
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-2),
            ContentDisposition = $"attachment; filename=\"{Path.GetFileName(downloadFileName)}\"",
        };

        return Task.FromResult<Uri?>(blob.GenerateSasUri(sas));
    }

    public async Task PurgeTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(TenantStorageKey.ContainerFor(tenantId));
        await containerClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        logger.LogWarning("Purged the storage container for tenant {TenantId}.", tenantId);
    }

    private BlobClient GetBlobClient(string storageKey)
    {
        (string container, string relativeKey) = TenantStorageKey.Resolve(tenantContext, storageKey);
        return serviceClient.GetBlobContainerClient(container).GetBlobClient(relativeKey);
    }
}
