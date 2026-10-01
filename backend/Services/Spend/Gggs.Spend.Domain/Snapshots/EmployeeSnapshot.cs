using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Snapshots;

/// <summary>
/// A read-only, eventually-consistent copy of just enough Core HR employee data for this service's
/// own needs - architecture.md §5.2, same pattern as <c>Gggs.Payroll.Domain.Snapshots.EmployeeSnapshot</c>.
/// Spend's needs are the same narrow shape: expense claim submission only needs enough to confirm
/// the employee exists and is not exited.
/// </summary>
public sealed class EmployeeSnapshot : TenantAggregateRoot
{
    private EmployeeSnapshot()
    {
        EmployeeCode = null!;
        DisplayName = null!;
        Status = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>String, matching how <c>EmployeeAssignmentChangedIntegrationEvent</c> carries it.</summary>
    public string Status { get; private set; }

    public DateOnly DateOfJoining { get; private set; }

    public int Version { get; private set; }

    public static EmployeeSnapshot Create(
        Guid employeeId, string employeeCode, string displayName, string status, DateOnly dateOfJoining, int version) => new()
        {
            Id = Guard.NotEmpty(employeeId),
            EmployeeId = employeeId,
            EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30),
            DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200),
            Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20),
            DateOfJoining = dateOfJoining,
            Version = version,
            IsActive = true,
        };

    public void Apply(string employeeCode, string displayName, string status, DateOnly dateOfJoining, int version)
    {
        EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30);
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200);
        Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20);
        DateOfJoining = dateOfJoining;
        Version = version;
    }

    public void MarkExited(int version)
    {
        Status = "Exited";
        Version = version;
    }
}
