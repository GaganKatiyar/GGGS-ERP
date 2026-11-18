using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Employee.Domain;

/// <summary>A file kept with a hire draft until the hire: the photo, or a document such as the Aadhaar card.</summary>
public sealed record HireDraftFile(string Slot, Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// A Hire form saved part-way, to be finished later. It is not an employee: it has no code, appears in no
/// directory, payroll or report, and becomes an employee only when someone presses Hire. What was typed is
/// kept encrypted as one block, because it holds Aadhaar, PAN, passport and bank details; the server never
/// reads inside it. Only the name and the step are in the clear, for the list.
/// </summary>
public sealed class HireDraft : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HireDraft()
    {
        DisplayName = null!;
        PayloadCiphertext = null!;
        FilesJson = "[]";
    }

    public string DisplayName { get; private set; }

    /// <summary>The wizard step it was left on, so it opens where the person stopped.</summary>
    public int Step { get; private set; }

    /// <summary>The accepted offer it hires from, when it came from Recruitment.</summary>
    public Guid? OfferId { get; private set; }

    public string PayloadCiphertext { get; private set; }

    public string FilesJson { get; private set; }

    public IReadOnlyList<HireDraftFile> Files => JsonSerializer.Deserialize<List<HireDraftFile>>(FilesJson, Json) ?? [];

    public static HireDraft Start(string displayName, int step, Guid? offerId, string payloadCiphertext)
    {
        var draft = new HireDraft();
        draft.Update(displayName, step, offerId, payloadCiphertext);
        return draft;
    }

    public void Update(string displayName, int step, Guid? offerId, string payloadCiphertext)
    {
        DisplayName = Guard.MaxLength(string.IsNullOrWhiteSpace(displayName) ? "Unnamed hire" : displayName.Trim(), 200);
        Step = Math.Clamp(step, 0, 20);
        OfferId = offerId;
        PayloadCiphertext = Guard.NotNullOrWhiteSpace(payloadCiphertext);
    }

    /// <summary>Puts a file in its slot; returns the one it replaced, whose stored copy the caller removes.</summary>
    public HireDraftFile? PutFile(HireDraftFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        List<HireDraftFile> files = [.. Files];
        HireDraftFile? replaced = files.FirstOrDefault(existing => existing.Slot == file.Slot);
        files.RemoveAll(existing => existing.Slot == file.Slot);
        files.Add(file);
        FilesJson = JsonSerializer.Serialize(files, Json);
        return replaced;
    }

    public HireDraftFile? RemoveFile(string slot)
    {
        List<HireDraftFile> files = [.. Files];
        HireDraftFile? removed = files.FirstOrDefault(existing => existing.Slot == slot);
        files.RemoveAll(existing => existing.Slot == slot);
        FilesJson = JsonSerializer.Serialize(files, Json);
        return removed;
    }
}
