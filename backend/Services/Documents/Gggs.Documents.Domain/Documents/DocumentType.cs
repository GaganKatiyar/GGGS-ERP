using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Documents.Domain.Documents;

/// <summary>What kind of thing a document type is filed under, for grouping in an admin console.</summary>
public enum DocumentCategory
{
    Employee = 1,
    Company = 2,
    Payroll = 3,
    Recruitment = 4,
}

/// <summary>
/// A tenant-defined kind of document - "Passport", "Offer Letter", "Employee Photo" - and the rules
/// an upload against it must satisfy.
/// <para>
/// Per-type view permissions (architecture.md §15) are not built yet: every document, whatever its
/// type, is gated by the same <c>document.file.view</c>/<c>document.file.upload</c> pair. A tenant
/// that needs "only Payroll admins can see payslip PDFs" enforced independently of general document
/// access does not get that in this slice.
/// </para>
/// </summary>
public sealed class DocumentType : TenantAggregateRoot
{
    private DocumentType()
    {
        Code = null!;
        Name = null!;
        AllowedExtensionsCsv = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public DocumentCategory Category { get; private set; }

    /// <summary>Lower-case, no leading dot, comma-separated - e.g. <c>pdf,jpg,png</c>.</summary>
    public string AllowedExtensionsCsv { get; private set; }

    public int MaxSizeMb { get; private set; }

    /// <summary>Whether an employee may upload against this type themselves through ESS, once it exists.</summary>
    public bool IsEmployeeUploadable { get; private set; }

    /// <summary>Sensitive types get no special handling yet beyond this flag being visible to admins
    /// deciding who to grant <c>document.file.view</c> to - see this class's own remarks.</summary>
    public bool IsSensitive { get; private set; }

    public static DocumentType Create(
        string code,
        string name,
        DocumentCategory category,
        IReadOnlyCollection<string> allowedExtensions,
        int maxSizeMb,
        bool isEmployeeUploadable,
        bool isSensitive)
    {
        Guard.Against(maxSizeMb is < 1 or > 500, "DOCUMENT_TYPE_MAX_SIZE_OUT_OF_RANGE", "Max size must be between 1 and 500 MB.");

        return new DocumentType
        {
            Code = NormalizeCode(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            Category = category,
            AllowedExtensionsCsv = NormalizeExtensions(allowedExtensions),
            MaxSizeMb = maxSizeMb,
            IsEmployeeUploadable = isEmployeeUploadable,
            IsSensitive = isSensitive,
        };
    }

    public void UpdateRules(IReadOnlyCollection<string> allowedExtensions, int maxSizeMb, bool isEmployeeUploadable, bool isSensitive)
    {
        Guard.Against(maxSizeMb is < 1 or > 500, "DOCUMENT_TYPE_MAX_SIZE_OUT_OF_RANGE", "Max size must be between 1 and 500 MB.");

        AllowedExtensionsCsv = NormalizeExtensions(allowedExtensions);
        MaxSizeMb = maxSizeMb;
        IsEmployeeUploadable = isEmployeeUploadable;
        IsSensitive = isSensitive;
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    /// <summary>True when a candidate file's extension is one this type accepts.</summary>
    public bool AllowsExtension(string extension)
    {
        string normalized = extension.TrimStart('.').Trim().ToLowerInvariant();
        return AllowedExtensionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(normalized);
    }

    public static string NormalizeCode(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 50);

    private static string NormalizeExtensions(IReadOnlyCollection<string> extensions)
    {
        Guard.Against(extensions.Count == 0, "DOCUMENT_TYPE_NO_EXTENSIONS", "At least one file extension must be allowed.");

        List<string> normalized = [.. extensions
            .Select(extension => extension.TrimStart('.').Trim().ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct()];

        Guard.Against(normalized.Count == 0, "DOCUMENT_TYPE_NO_EXTENSIONS", "At least one file extension must be allowed.");

        return string.Join(',', normalized);
    }
}
