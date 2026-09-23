namespace Gggs.BuildingBlocks.Files;

/// <summary>A file on its way into storage.</summary>
public sealed record FileToStore
{
    public required Stream Content { get; init; }

    /// <summary>
    /// Path within the tenant's own storage area, for example
    /// <c>employee/{employeeId}/{documentGroupId}/v1/passport.pdf</c>. The tenant segment is added
    /// by the adapter and is never supplied by callers.
    /// </summary>
    public required string RelativeKey { get; init; }

    public required string ContentType { get; init; }

    /// <summary>Written as blob metadata or a sidecar; useful when reconciling storage against the database.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>Where a stored file ended up, and what it hashed to.</summary>
public sealed record StoredFile(string StorageKey, long SizeBytes, string Sha256);

/// <summary>
/// Document storage, with the tenant boundary built into the adapter rather than left to callers.
/// <para>
/// Every implementation resolves the tenant from the ambient context and refuses a key that belongs
/// to another tenant, so a handler cannot read across the boundary even if it is handed a key it
/// should not have. This is layer 11 of the isolation design.
/// </para>
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(FileToStore file, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived, single-file download link, or <c>null</c> when the backing store cannot issue
    /// one (the on-premise file share, for instance) and the API should stream the file itself.
    /// </summary>
    Task<Uri?> TryCreateDownloadLinkAsync(
        string storageKey,
        TimeSpan validFor,
        string downloadFileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes everything belonging to a tenant. Used at the end of offboarding, after the export
    /// and the grace period, and only from a platform-scoped job.
    /// </summary>
    Task PurgeTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
