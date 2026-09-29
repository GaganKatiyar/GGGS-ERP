using Gggs.BuildingBlocks.Messaging;

namespace Gggs.CoreHr.Contracts;

/*
 * Core HR's published contract.
 *
 * Other services reference this package and nothing else of ours. Payloads carry ids plus the few
 * fields a consumer needs to act; anything more is fetched from the API, which applies its own
 * permissions.
 */

/// <summary>
/// An employee's current assignment changed - hire, transfer, promotion, or a reporting-manager
/// change alone. One event shape for all four, because every consumer that cares (today: Workflow,
/// resolving "who is this person's manager" for an approval step) only cares about the resulting
/// state, not which of <c>EmploymentChangeType</c>'s reasons produced it.
/// <para>
/// This is the source for every service's local <c>ref.EmployeeSnapshot</c> table
/// (architecture.md §5.2) - a read-only, eventually-consistent copy of just enough employee data to
/// resolve approvers, reporting lines and similar lookups without a synchronous call back to Core
/// HR on every request. <see cref="Version"/> is <c>EmploymentRecord</c>'s own row's insertion
/// order within the employee (1 for Hire, incrementing from there); consumers discard an event
/// whose version is not higher than what they already have, so out-of-order delivery cannot
/// overwrite newer data with older.
/// </para>
/// </summary>
[MessageType("gggs.corehr.employee.assignment-changed")]
public sealed record EmployeeAssignmentChangedIntegrationEvent(
    Guid EmployeeId,
    string EmployeeCode,
    string DisplayName,

    /// <summary>String, not the numeric enum - every service serializes enums by member name
    /// (<c>JsonConfiguration.Apply</c>), and the envelope payload goes through the same
    /// serializer.</summary>
    string Status,

    Guid? UserId,
    Guid LegalEntityId,
    Guid BranchId,
    Guid DepartmentId,
    Guid DesignationId,
    Guid? GradeId,
    Guid? ReportingManagerId,
    int Version) : IntegrationEvent;

/// <summary>
/// An employee exited (or was rehired, which is <see cref="EmployeeAssignmentChangedIntegrationEvent"/>
/// again since rehiring produces a fresh current record) - the one status change with no
/// accompanying <c>EmploymentRecord</c> row, so it is not covered by the event above.
/// </summary>
[MessageType("gggs.corehr.employee.exited")]
public sealed record EmployeeExitedIntegrationEvent(Guid EmployeeId, int Version) : IntegrationEvent;
