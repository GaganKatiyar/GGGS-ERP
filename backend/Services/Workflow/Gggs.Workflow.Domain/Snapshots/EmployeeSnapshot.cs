using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Workflow.Domain.Snapshots;

/// <summary>
/// A read-only, eventually-consistent copy of just enough Core HR employee data to resolve
/// approvers without a synchronous call back to Core HR on every request (architecture.md §5.2).
/// Fed by <see cref="Gggs.CoreHr.Contracts.EmployeeAssignmentChangedIntegrationEvent"/> and
/// <see cref="Gggs.CoreHr.Contracts.EmployeeExitedIntegrationEvent"/> - never written by anything
/// else in this service.
/// <para>
/// <see cref="Version"/> guards against out-of-order delivery: a consumer discards an incoming
/// event whose version is not strictly greater than the row it already has, so a message that
/// arrives late can never overwrite a newer state with a stale one.
/// </para>
/// </summary>
public sealed class EmployeeSnapshot : TenantAggregateRoot
{
    private EmployeeSnapshot()
    {
        EmployeeCode = null!;
        DisplayName = null!;
        Status = null!;
    }

    /// <summary>Core HR's <c>Employee.Id</c> - not this row's own identity concept, but the
    /// natural key every lookup uses, so <c>Id</c> (inherited from <see cref="TenantAggregateRoot"/>)
    /// is set equal to it at creation rather than left to generate independently.</summary>
    public Guid EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>String, matching how <c>EmployeeAssignmentChangedIntegrationEvent</c> carries it -
    /// see that event's own remarks.</summary>
    public string Status { get; private set; }

    public Guid? UserId { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid DesignationId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public int Version { get; private set; }

    public static EmployeeSnapshot Create(
        Guid employeeId,
        string employeeCode,
        string displayName,
        string status,
        Guid? userId,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        Guid designationId,
        Guid? gradeId,
        Guid? managerEmployeeId,
        int version) => new()
        {
            Id = Guard.NotEmpty(employeeId),
            EmployeeId = employeeId,
            EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30),
            DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200),
            Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20),
            UserId = userId,
            LegalEntityId = Guard.NotEmpty(legalEntityId),
            BranchId = Guard.NotEmpty(branchId),
            DepartmentId = Guard.NotEmpty(departmentId),
            DesignationId = Guard.NotEmpty(designationId),
            GradeId = gradeId,
            ManagerEmployeeId = managerEmployeeId,
            Version = version,
            IsActive = true,
        };

    /// <summary>Applies a newer event's data onto this existing row. The caller checks
    /// <see cref="Version"/> before calling this - see <c>EmployeeSnapshotConsumer</c>.</summary>
    public void Apply(
        string employeeCode,
        string displayName,
        string status,
        Guid? userId,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        Guid designationId,
        Guid? gradeId,
        Guid? managerEmployeeId,
        int version)
    {
        EmployeeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employeeCode), 30);
        DisplayName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(displayName), 200);
        Status = Guard.MaxLength(Guard.NotNullOrWhiteSpace(status), 20);
        UserId = userId;
        LegalEntityId = Guard.NotEmpty(legalEntityId);
        BranchId = Guard.NotEmpty(branchId);
        DepartmentId = Guard.NotEmpty(departmentId);
        DesignationId = Guard.NotEmpty(designationId);
        GradeId = gradeId;
        ManagerEmployeeId = managerEmployeeId;
        Version = version;
    }

    /// <summary>Marks the employee gone without deleting the row - an in-flight instance whose
    /// approver just exited still needs to resolve to someone, and the fallback-approver path
    /// (architecture.md §13.3 step 8) reads this rather than getting a 404-shaped hole.</summary>
    public void MarkExited(int version)
    {
        Status = "Exited";
        Version = version;
    }
}
