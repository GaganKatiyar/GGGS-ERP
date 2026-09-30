using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Snapshots;

/// <summary>
/// A read-only, eventually-consistent copy of just enough Core HR employee data for this service's
/// own needs - architecture.md §5.2, same pattern as <c>Gggs.Time.Domain.Snapshots.EmployeeSnapshot</c>
/// and <c>Gggs.Workflow.Domain.Snapshots.EmployeeSnapshot</c>. Talent only needs a display name for
/// the hiring manager shown against a requisition, so this carries the fewest fields of the three.
/// </summary>
public sealed class EmployeeSnapshot : TenantAggregateRoot
{
    private EmployeeSnapshot()
    {
        EmployeeCode = null!;
        DisplayName = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; }

    public string DisplayName { get; private set; }

    public int Version { get; private set; }

    public static EmployeeSnapshot Create(Guid employeeId, string employeeCode, string displayName, int version) => new()
    {
        Id = Guard.NotEmpty(employeeId),
        EmployeeId = employeeId,
        EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30),
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200),
        Version = version,
        IsActive = true,
    };

    public void Apply(string employeeCode, string displayName, int version)
    {
        EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30);
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200);
        Version = version;
    }
}
