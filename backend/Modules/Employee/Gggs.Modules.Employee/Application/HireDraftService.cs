using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Employee.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Application;

/// <summary>One saved draft in the Draft hires list.</summary>
public sealed record HireDraftItem(Guid Id, string DisplayName, int Step, Guid? OfferId, IReadOnlyList<string> Files, DateTime CreatedDate, DateTime? ModifiedDate);

/// <summary>A draft opened to carry on: what was typed, exactly as the form sent it, and the files kept with it.</summary>
public sealed record HireDraftDetail(Guid Id, string DisplayName, int Step, Guid? OfferId, JsonElement Payload, IReadOnlyList<HireDraftFile> Files);

/// <summary>What the form sends to save a draft. <c>Payload</c> is the form's own value; the server only stores it.</summary>
public sealed record HireDraftBody(string? DisplayName, int Step, Guid? OfferId, JsonElement Payload);

/// <summary>A file read back from a draft.</summary>
public sealed record HireDraftFileContent(Stream Content, string ContentType, string FileName);

/// <summary>
/// Hire drafts: a Hire form saved part-way and finished later, by the same person or another with the
/// right to hire. The form's value is encrypted whole; the photo and documents wait in tenant file storage
/// until the hire, when the form uploads them to the new employee and deletes the draft.
/// </summary>
public sealed class HireDraftService(
    AppDbContext dbContext,
    IUnitOfWork unitOfWork,
    IFileStorage fileStorage,
    ITenantContext tenantContext,
    ISensitiveDataProtector protector)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxPhotoBytes = EmployeePhotoService.MaxBytes;
    public const int MaxPayloadLength = 100_000;

    /// <summary>The photo and the documents the Hire form asks for.</summary>
    public static readonly IReadOnlyList<string> Slots = ["photo", "AADHAAR", "PAN", "PASSPORT", "OFFER_LETTER"];

    public async Task<IReadOnlyList<HireDraftItem>> ListAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.HireDrafts.AsNoTracking().OrderByDescending(draft => draft.ModifiedDate ?? draft.CreatedDate).ToListAsync(cancellationToken))
            .Select(draft => new HireDraftItem(draft.Id, draft.DisplayName, draft.Step, draft.OfferId, [.. draft.Files.Select(file => file.Slot)], draft.CreatedDate, draft.ModifiedDate))];

    public async Task<HireDraftDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        HireDraft draft = await FindAsync(id, tracked: false, cancellationToken);
        string payload = protector.RevealText(SensitiveDataPurposes.HireDraft, draft.PayloadCiphertext);
        using JsonDocument document = JsonDocument.Parse(payload);
        return new HireDraftDetail(draft.Id, draft.DisplayName, draft.Step, draft.OfferId, document.RootElement.Clone(), draft.Files);
    }

    public async Task<HireDraftDetail> SaveAsync(Guid? id, HireDraftBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new DomainRuleException("HIRE_DRAFT_PAYLOAD", "There is nothing to save.");
        }

        string payload = body.Payload.GetRawText();
        if (payload.Length > MaxPayloadLength)
        {
            throw new DomainRuleException("HIRE_DRAFT_PAYLOAD", "The draft is too large to save.");
        }

        string ciphertext = protector.ProtectText(SensitiveDataPurposes.HireDraft, payload);
        HireDraft draft;
        if (id is { } existing)
        {
            draft = await FindAsync(existing, tracked: true, cancellationToken);
            draft.Update(body.DisplayName ?? string.Empty, body.Step, body.OfferId, ciphertext);
        }
        else
        {
            draft = HireDraft.Start(body.DisplayName ?? string.Empty, body.Step, body.OfferId, ciphertext);
            dbContext.HireDrafts.Add(draft);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(draft.Id, cancellationToken);
    }

    /// <summary>Removes the draft and every file kept with it: after the hire, or when it is given up.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        HireDraft draft = await FindAsync(id, tracked: true, cancellationToken);
        IReadOnlyList<HireDraftFile> files = draft.Files;
        dbContext.HireDrafts.Remove(draft);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (HireDraftFile file in files)
        {
            await DeleteStoredAsync(draft.Id, file);
        }
    }

    /// <summary>
    /// Keeps a file in its slot, replacing what was there. Only pictures (JPEG, PNG, WebP) and PDFs are taken,
    /// judged by the file's first bytes, never its name; the photo slot takes pictures only.
    /// </summary>
    public async Task<HireDraftFile> PutFileAsync(Guid id, string slot, string? fileName, long length, Func<Stream> open, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        string known = Slots.FirstOrDefault(candidate => string.Equals(candidate, slot, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("Draft file", slot);
        bool photo = known == "photo";
        if (length <= 0)
        {
            throw new DomainRuleException("HIRE_DRAFT_FILE_EMPTY", "The file is empty.");
        }

        if (length > (photo ? MaxPhotoBytes : MaxFileBytes))
        {
            throw new DomainRuleException("HIRE_DRAFT_FILE_TOO_LARGE", photo ? "A photo can be at most 2 MB." : "A document can be at most 10 MB.");
        }

        byte[] head = new byte[16];
        int read;
        using (Stream peek = open())
        {
            read = peek.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        string contentType = EmployeePhotoService.Sniff(head.AsSpan(0, read))
            ?? (!photo && IsPdf(head.AsSpan(0, read)) ? "application/pdf" : null)
            ?? throw new DomainRuleException("HIRE_DRAFT_FILE_TYPE", photo ? "Upload a JPEG, PNG or WebP picture." : "Upload a PDF or a JPEG, PNG or WebP picture.");

        HireDraft draft = await FindAsync(id, tracked: true, cancellationToken);
        var file = new HireDraftFile(known, Guid.CreateVersion7(), SafeName(fileName, known), contentType, length);
        StoredFile stored;
        await using (Stream content = open())
        {
            stored = await fileStorage.SaveAsync(new FileToStore { Content = content, RelativeKey = RelativeKey(draft.Id, file), ContentType = contentType }, cancellationToken);
        }

        HireDraftFile? replaced = draft.PutFile(file);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        if (replaced is not null)
        {
            await DeleteStoredAsync(draft.Id, replaced);
        }

        return file;
    }

    public async Task<HireDraftFileContent> OpenFileAsync(Guid id, string slot, CancellationToken cancellationToken)
    {
        HireDraft draft = await FindAsync(id, tracked: false, cancellationToken);
        HireDraftFile file = draft.Files.FirstOrDefault(candidate => string.Equals(candidate.Slot, slot, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("Draft file", slot);
        Stream content = await fileStorage.OpenReadAsync(TenantStorageKey.Build(tenantContext, RelativeKey(draft.Id, file)), cancellationToken);
        return new HireDraftFileContent(content, file.ContentType, file.FileName);
    }

    public async Task RemoveFileAsync(Guid id, string slot, CancellationToken cancellationToken)
    {
        HireDraft draft = await FindAsync(id, tracked: true, cancellationToken);
        if (draft.RemoveFile(Slots.FirstOrDefault(candidate => string.Equals(candidate, slot, StringComparison.OrdinalIgnoreCase)) ?? slot) is { } removed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await DeleteStoredAsync(draft.Id, removed);
        }
    }

    private async Task<HireDraft> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<HireDraft> drafts = tracked ? dbContext.HireDrafts : dbContext.HireDrafts.AsNoTracking();
        return await drafts.FirstOrDefaultAsync(draft => draft.Id == id, cancellationToken) ?? throw new NotFoundException("Hire draft", id);
    }

    private async Task DeleteStoredAsync(Guid draftId, HireDraftFile file)
    {
        try
        {
            await fileStorage.DeleteAsync(TenantStorageKey.Build(tenantContext, RelativeKey(draftId, file)), CancellationToken.None);
        }
        catch (IOException)
        {
            // Nothing points at it any more; a leftover file is harmless and the tenant purge removes it.
        }
    }

    private static bool IsPdf(ReadOnlySpan<byte> head) => head.Length >= 5 && head[..5].SequenceEqual("%PDF-"u8);

    /// <summary>The name shown back and used on download: no folders, nothing that could break a header.</summary>
    private static string SafeName(string? fileName, string slot)
    {
        string name = Path.GetFileName(fileName ?? string.Empty);
        name = new string([.. name.Where(character => !char.IsControl(character) && character is not '"' and not '\\' and not '/')]).Trim();
        return name.Length == 0 ? slot.ToLowerInvariant() : name.Length > 120 ? name[^120..] : name;
    }

    private static string RelativeKey(Guid draftId, HireDraftFile file) => $"hire-draft/{draftId:N}/{file.Slot.ToLowerInvariant()}-{file.FileId:N}";
}
