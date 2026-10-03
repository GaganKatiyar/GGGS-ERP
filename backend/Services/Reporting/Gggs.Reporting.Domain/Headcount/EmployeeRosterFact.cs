using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Reporting.Domain.Headcount;

/// <summary>
/// One row per employee, kept current from Core HR's own events - the same
/// snapshot-fed-by-events pattern used everywhere else (architecture.md §5.2), just with the org
/// dimensions a headcount fact needs rather than the display fields Payroll/Spend keep.
/// <para>
/// This is Reporting's only source of truth for "who currently works where". The nightly
/// <c>HeadcountDailySnapshot</c> is a derived aggregate over this table, never the other way
/// round, so a department re-org or a late-arriving correction is reflected in every future
/// snapshot without needing to replay history.
/// </para>
/// </summary>
public sealed class EmployeeRosterFact : TenantAggregateRoot
{
    private EmployeeRosterFact()
    {
        EmployeeCode = null!;
        Status = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; }

    /// <summary>String, matching how <c>EmployeeAssignmentChangedIntegrationEvent</c> carries it.</summary>
    public string Status { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public DateOnly DateOfJoining { get; private set; }

    /// <summary>Set once the employee exits; the roster keeps the row (for history / last-known
    /// org placement) instead of deleting it, since a departed employee still belongs in the
    /// leavers count for the day they left.</summary>
    public DateOnly? ExitDate { get; private set; }

    public int Version { get; private set; }

    public static EmployeeRosterFact Create(
        Guid employeeId,
        string employeeCode,
        string status,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        DateOnly dateOfJoining,
        int version) => new()
        {
            Id = Guard.NotEmpty(employeeId),
            EmployeeId = employeeId,
            EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30),
            Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20),
            LegalEntityId = Guard.NotEmpty(legalEntityId),
            BranchId = Guard.NotEmpty(branchId),
            DepartmentId = Guard.NotEmpty(departmentId),
            DateOfJoining = dateOfJoining,
            Version = version,
            IsActive = true,
        };

    public void Apply(
        string employeeCode,
        string status,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        DateOnly dateOfJoining,
        int version)
    {
        EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30);
        Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20);
        LegalEntityId = Guard.NotEmpty(legalEntityId);
        BranchId = Guard.NotEmpty(branchId);
        DepartmentId = Guard.NotEmpty(departmentId);
        DateOfJoining = dateOfJoining;
        Version = version;
    }

    public void MarkExited(DateOnly exitDate, int version)
    {
        Status = "Exited";
        ExitDate = exitDate;
        Version = version;
    }
}
