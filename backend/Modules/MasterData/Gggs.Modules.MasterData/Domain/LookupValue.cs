using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.MasterData.Domain;

/// <summary>
/// One entry in one of a tenant's lookup lists - "IN / India" in <c>country</c>, "HYB / Hybrid" in
/// <c>work-mode</c>.
/// <para>
/// The code is what other records store and never changes once created; the label, order and
/// active flag are the tenant's to edit. Values seeded with the tenant are marked
/// <see cref="IsSystem"/> and can be deactivated but not deleted, because other modules may already
/// hold their codes.
/// </para>
/// </summary>
public sealed class LookupValue : TenantAggregateRoot
{
    private LookupValue()
    {
        ListCode = null!;
        Code = null!;
        Label = null!;
    }

    public string ListCode { get; private set; }

    public string Code { get; private set; }

    public string Label { get; private set; }

    /// <summary>For hierarchical lists: the parent's code (a state's country).</summary>
    public string? ParentCode { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsSystem { get; private set; }

    public static LookupValue Create(
        string listCode,
        string code,
        string label,
        string? parentCode = null,
        int sortOrder = 0,
        bool isSystem = false) => new()
        {
            ListCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(listCode).Trim().ToLowerInvariant(), 32),
            Code = NormalizeCode(code),
            Label = Guard.MaxLength(Guard.NotNullOrWhiteSpace(label).Trim(), 200),
            ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : NormalizeCode(parentCode),
            SortOrder = sortOrder,
            IsSystem = isSystem,
        };

    /// <summary>Codes are compared case-insensitively everywhere, so they are stored upper-case.</summary>
    public static string NormalizeCode(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 64);

    public void Update(string label, int sortOrder)
    {
        Label = Guard.MaxLength(Guard.NotNullOrWhiteSpace(label).Trim(), 200);
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
