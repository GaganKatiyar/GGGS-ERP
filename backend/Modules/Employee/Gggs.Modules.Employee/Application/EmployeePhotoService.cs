using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Application;

/// <summary>A picture on its way in: its size and a way to read it, which may be called more than once.</summary>
public sealed record IncomingPhoto(long Length, Func<Stream> Open);

/// <summary>Which photo an employee has, if any. The id changes with every new photo, so it is the cache version.</summary>
public sealed record PhotoInfo(Guid? PhotoId);

/// <summary>A photo read back for display, with the type its own bytes declare.</summary>
public sealed record PhotoContent(Stream Content, string ContentType, Guid PhotoId);

/// <summary>
/// Profile photos. They live in tenant file storage under the employee rather than in Documents: a
/// photo has no type, versions, expiry or signature, and does not belong among a person's documents.
/// <para>
/// Only JPEG, PNG and WebP are accepted, judged by the file's first bytes, never by its name or the
/// declared content type. The type is judged again on the way out, so a file changed in storage
/// cannot be served as anything but an image.
/// </para>
/// </summary>
public sealed class EmployeePhotoService(
    AppDbContext dbContext,
    IUnitOfWork unitOfWork,
    IFileStorage fileStorage,
    ITenantContext tenantContext,
    EmployeeScope scope)
{
    /// <summary>The browser scales a photo down to 512 px before sending it, so this is generous.</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    public async Task<PhotoInfo> GetInfoAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => new(await dbContext.Employees.AsNoTracking()
            .Where(employee => employee.Id == employeeId)
            .Select(employee => employee.PhotoId)
            .FirstOrDefaultAsync(cancellationToken));

    /// <summary>Stores a new photo for the employee and removes the one it replaces.</summary>
    public async Task<PhotoInfo> ReplaceAsync(Guid employeeId, IncomingPhoto photo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        string contentType = Inspect(photo);

        EmployeeEntity employee = await dbContext.Employees.FirstOrDefaultAsync(candidate => candidate.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

        Guid? previous = employee.PhotoId;
        Guid photoId = Guid.CreateVersion7();

        StoredFile stored;
        await using (Stream content = photo.Open())
        {
            stored = await fileStorage.SaveAsync(
                new FileToStore { Content = content, RelativeKey = RelativeKey(employeeId, photoId), ContentType = contentType },
                cancellationToken);
        }

        employee.SetPhoto(photoId);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing points at the new file: remove it rather than leave an orphan.
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        if (previous is { } old)
        {
            await DeleteFileAsync(employeeId, old);
        }

        return new PhotoInfo(photoId);
    }

    public async Task RemoveAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        EmployeeEntity employee = await dbContext.Employees.FirstOrDefaultAsync(candidate => candidate.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

        if (employee.PhotoId is not { } old)
        {
            return;
        }

        employee.RemovePhoto();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteFileAsync(employeeId, old);
    }

    /// <summary>The signed-in employee's own photo, or null when they have none.</summary>
    public async Task<PhotoContent?> OpenOwnAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await OpenAsync(dbContext.Employees.AsNoTracking().Where(employee => employee.Id == employeeId), employeeId, cancellationToken);

    /// <summary>Another employee's photo, within the caller's employee-view scope; null when out of scope or none.</summary>
    public async Task<PhotoContent?> OpenAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);
        return await OpenAsync(visible.Where(employee => employee.Id == employeeId), employeeId, cancellationToken);
    }

    /// <summary>The image type the first bytes declare, or null for anything else.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        // RIFF....WEBP
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private async Task<PhotoContent?> OpenAsync(IQueryable<EmployeeEntity> query, Guid employeeId, CancellationToken cancellationToken)
    {
        Guid? photoId = await query.Select(employee => employee.PhotoId).FirstOrDefaultAsync(cancellationToken);
        if (photoId is not { } id)
        {
            return null;
        }

        // Photos are small: read the whole file so its type can be checked before anything is sent.
        MemoryStream buffer = new();
        await using (Stream stored = await fileStorage.OpenReadAsync(StorageKey(employeeId, id), cancellationToken))
        {
            await stored.CopyToAsync(buffer, cancellationToken);
        }

        string? contentType = Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (contentType is null)
        {
            await buffer.DisposeAsync();
            return null;
        }

        buffer.Position = 0;
        return new PhotoContent(buffer, contentType, id);
    }

    private static string Inspect(IncomingPhoto photo)
    {
        if (photo.Length <= 0)
        {
            throw new DomainRuleException("PHOTO_EMPTY", "The file is empty.");
        }

        if (photo.Length > MaxBytes)
        {
            throw new DomainRuleException("PHOTO_TOO_LARGE", "A photo can be at most 2 MB.");
        }

        byte[] head = new byte[16];
        int read;
        using (Stream peek = photo.Open())
        {
            read = peek.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        return Sniff(head.AsSpan(0, read))
            ?? throw new DomainRuleException("PHOTO_TYPE_NOT_ALLOWED", "Upload a JPEG, PNG or WebP picture.");
    }

    private async Task DeleteFileAsync(Guid employeeId, Guid photoId)
    {
        try
        {
            await fileStorage.DeleteAsync(StorageKey(employeeId, photoId), CancellationToken.None);
        }
        catch (IOException)
        {
            // The database no longer points at it; a leftover file is harmless and the tenant purge removes it.
        }
    }

    private string StorageKey(Guid employeeId, Guid photoId) => TenantStorageKey.Build(tenantContext, RelativeKey(employeeId, photoId));

    private static string RelativeKey(Guid employeeId, Guid photoId) => $"employee/{employeeId:N}/photo/{photoId:N}";
}
