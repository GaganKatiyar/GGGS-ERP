using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Engagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Engagement.Application;

public sealed record FeatureBannerItem(
    Guid Id,
    string Title,
    string? Body,
    BannerKind Kind,
    string? ActionLabel,
    string? ActionLink,
    string? SecondaryLabel,
    string? SecondaryLink,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int Position,
    bool IsPublished,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    string? ImageUrl,
    BannerLayout Layout);

/// <summary>A slide as an employee sees it, without who else it was written for.</summary>
public sealed record MyFeatureBanner(
    Guid Id,
    string Title,
    string? Body,
    BannerKind Kind,
    string? ActionLabel,
    string? ActionLink,
    string? SecondaryLabel,
    string? SecondaryLink,
    DateOnly? EndsOn,
    string? ImageUrl,
    BannerLayout Layout);

/// <summary>A picture on its way in: its size and a way to read it, which may be called more than once.</summary>
public sealed record IncomingBannerImage(long Length, Func<Stream> Open);

/// <summary>A picture read back for display, with the type its own bytes declare.</summary>
public sealed record BannerImage(Stream Content, string ContentType, Guid ImageId);

/// <summary>
/// The slides of the home dashboard's banner: managed by HR, shown by date and audience. Pictures live
/// in tenant file storage. Only JPEG, PNG and WebP are accepted, judged by the file's first bytes and
/// judged again on the way out, so a changed file cannot be served as anything but an image.
/// </summary>
internal sealed class FeatureBannerService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    ICurrentUser currentUser,
    ITenantClock clock,
    IFileStorage fileStorage,
    ITenantContext tenantContext)
{
    /// <summary>More slides than this and nobody reaches the last ones.</summary>
    public const int MostShown = 8;

    /// <summary>The browser scales a picture down before sending it, so this is generous.</summary>
    public const long MaxImageBytes = 3 * 1024 * 1024;

    public async Task<IReadOnlyList<FeatureBannerItem>> ListAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.FeatureBanners.AsNoTracking()
                .OrderBy(row => row.Position)
                .ThenByDescending(row => row.StartsOn)
                .ToListAsync(cancellationToken))
            .Select(Item)];

    public async Task<FeatureBannerItem> SaveAsync(Guid? id, FeatureBannerContent body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        FeatureBanner banner;
        if (id is { } existing)
        {
            banner = await FindAsync(existing, cancellationToken);
            banner.Update(body);
        }
        else
        {
            banner = FeatureBanner.Draft(body);
            dbContext.FeatureBanners.Add(banner);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(banner);
    }

    public async Task<FeatureBannerItem> SetPublishedAsync(Guid id, bool published, CancellationToken cancellationToken)
    {
        FeatureBanner banner = await FindAsync(id, cancellationToken);
        if (published)
        {
            banner.Publish();
        }
        else
        {
            banner.Unpublish();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(banner);
    }

    /// <summary>Stores a new picture for the slide and removes the one it replaces.</summary>
    public async Task<FeatureBannerItem> ReplaceImageAsync(Guid id, IncomingBannerImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        string contentType = Inspect(image);
        FeatureBanner banner = await FindAsync(id, cancellationToken);
        Guid? previous = banner.ImageId;
        Guid imageId = Guid.CreateVersion7();

        StoredFile stored;
        await using (Stream content = image.Open())
        {
            stored = await fileStorage.SaveAsync(new FileToStore { Content = content, RelativeKey = RelativeKey(id, imageId), ContentType = contentType }, cancellationToken);
        }

        banner.SetImage(imageId);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing points at the new file: remove it rather than leave an orphan.
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        if (previous is { } old)
        {
            await DeleteFileAsync(id, old);
        }

        return Item(banner);
    }

    public async Task<FeatureBannerItem> RemoveImageAsync(Guid id, CancellationToken cancellationToken)
    {
        FeatureBanner banner = await FindAsync(id, cancellationToken);
        if (banner.ImageId is { } old)
        {
            banner.RemoveImage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await DeleteFileAsync(id, old);
        }

        return Item(banner);
    }

    /// <summary>Any slide's picture, for those who manage slides and so see drafts too.</summary>
    public async Task<BannerImage?> OpenImageAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid? imageId = await dbContext.FeatureBanners.AsNoTracking().Where(row => row.Id == id).Select(row => row.ImageId).FirstOrDefaultAsync(cancellationToken);
        return imageId is { } found ? await ReadImageAsync(id, found, cancellationToken) : null;
    }

    /// <summary>A slide's picture for the signed-in person: only while that slide is showing to them.</summary>
    public async Task<BannerImage?> OpenMyImageAsync(Guid id, CancellationToken cancellationToken)
    {
        FeatureBanner? banner = await dbContext.FeatureBanners.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (banner?.ImageId is not { } imageId)
        {
            return null;
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        EmployeeSummary? me = currentUser.EmployeeId is { } employeeId ? await employees.GetAsync(employeeId, cancellationToken) : null;
        return banner.IsShownTo(today, me?.DepartmentId, me?.LocationId) ? await ReadImageAsync(id, imageId, cancellationToken) : null;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        dbContext.FeatureBanners.Remove(await FindAsync(id, cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The slides showing today to the signed-in person. An account with no employee record has no
    /// department or location, so it sees only the slides meant for everyone.
    /// </summary>
    public async Task<IReadOnlyList<MyFeatureBanner>> MineAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<FeatureBanner> candidates = await dbContext.FeatureBanners.AsNoTracking()
            .Where(row => row.IsPublished && row.StartsOn <= today && (row.EndsOn == null || row.EndsOn >= today))
            .OrderBy(row => row.Position)
            .ThenByDescending(row => row.StartsOn)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        EmployeeSummary? me = currentUser.EmployeeId is { } employeeId ? await employees.GetAsync(employeeId, cancellationToken) : null;
        return [.. candidates
            .Where(row => row.IsShownTo(today, me?.DepartmentId, me?.LocationId))
            .Take(MostShown)
            .Select(row => new MyFeatureBanner(row.Id, row.Title, row.Body, row.Kind, row.ActionLabel, row.ActionLink, row.SecondaryLabel, row.SecondaryLink, row.EndsOn, row.ImageId is { } image ? $"/api/v1/me/banners/{row.Id}/image?v={image}" : null, row.Layout))];
    }

    private async Task<FeatureBanner> FindAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.FeatureBanners.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(FeatureBanner), id);

    private static FeatureBannerItem Item(FeatureBanner row)
        => new(row.Id, row.Title, row.Body, row.Kind, row.ActionLabel, row.ActionLink, row.SecondaryLabel, row.SecondaryLink, row.StartsOn, row.EndsOn, row.Position, row.IsPublished, row.AudienceDepartmentIds, row.AudienceLocationIds, row.ImageId is { } image ? $"/api/v1/engagement/banners/{row.Id}/image?v={image}" : null, row.Layout);

    private static string Inspect(IncomingBannerImage image)
    {
        if (image.Length <= 0)
        {
            throw new DomainRuleException("BANNER_IMAGE_EMPTY", "The file is empty.");
        }

        if (image.Length > MaxImageBytes)
        {
            throw new DomainRuleException("BANNER_IMAGE_TOO_LARGE", "A picture can be at most 3 MB.");
        }

        byte[] head = new byte[16];
        int read;
        using (Stream peek = image.Open())
        {
            read = peek.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        return ImageFormat.Sniff(head.AsSpan(0, read))
            ?? throw new DomainRuleException("BANNER_IMAGE_TYPE", "Upload a JPEG, PNG or WebP picture.");
    }

    private async Task<BannerImage?> ReadImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        // Small files: read the whole picture so its type can be checked before anything is sent.
        MemoryStream buffer = new();
        await using (Stream stored = await fileStorage.OpenReadAsync(StorageKey(id, imageId), cancellationToken))
        {
            await stored.CopyToAsync(buffer, cancellationToken);
        }

        string? contentType = ImageFormat.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (contentType is null)
        {
            await buffer.DisposeAsync();
            return null;
        }

        buffer.Position = 0;
        return new BannerImage(buffer, contentType, imageId);
    }

    private async Task DeleteFileAsync(Guid id, Guid imageId)
    {
        try
        {
            await fileStorage.DeleteAsync(StorageKey(id, imageId), CancellationToken.None);
        }
        catch (IOException)
        {
            // The database no longer points at it; a leftover file is harmless and the tenant purge removes it.
        }
    }

    private string StorageKey(Guid id, Guid imageId) => TenantStorageKey.Build(tenantContext, RelativeKey(id, imageId));

    private static string RelativeKey(Guid id, Guid imageId) => $"engagement/banner/{id:N}/{imageId:N}";
}
