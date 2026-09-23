using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Sample.Api.Domain;

/// <summary>
/// A minimal tenant-owned aggregate. It exists so the isolation, audit and concurrency machinery
/// can be tested against a real database without dragging in HR domain rules.
/// </summary>
public sealed class SampleRecord : TenantAggregateRoot
{
    private SampleRecord()
    {
        // EF materialization.
        Code = null!;
        Title = null!;
    }

    private SampleRecord(string code, string title, string? notes)
    {
        Code = code;
        Title = title;
        Notes = notes;
    }

    /// <summary>Unique within the tenant, not globally: two tenants may both use "ACME-01".</summary>
    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Notes { get; private set; }

    public static SampleRecord Create(string code, string title, string? notes = null)
    {
        Guard.NotNullOrWhiteSpace(code);
        Guard.NotNullOrWhiteSpace(title);
        Guard.MaxLength(code, 30);
        Guard.MaxLength(title, 200);

        return new SampleRecord(code.Trim().ToUpperInvariant(), title.Trim(), notes?.Trim());
    }

    public void Rename(string title)
    {
        Guard.NotNullOrWhiteSpace(title);
        Guard.MaxLength(title, 200);
        Title = title.Trim();
    }

    public void Deactivate() => IsActive = false;
}
