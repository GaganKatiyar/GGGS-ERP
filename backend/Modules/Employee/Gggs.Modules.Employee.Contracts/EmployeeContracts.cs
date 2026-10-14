using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Employee.Contracts;

/// <summary>An employee as other modules need them: who, and where they sit right now.</summary>
public sealed record EmployeeSummary(
    Guid Id,
    string EmployeeCode,
    string DisplayName,
    string? OfficialEmail,
    string Status,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    Guid? LegalEntityId);

/// <summary>
/// The employee directory other modules read (ADR-12). It replaces the five replicated employee
/// snapshot tables of the old services: one source, read in-process.
/// </summary>
public interface IEmployeeDirectory
{
    Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Whether the id is an employee of the current tenant who has not exited.</summary>
    Task<bool> IsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>How many active employees currently hold each position - a position's occupancy.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByPositionAsync(IEnumerable<Guid> positionIds, CancellationToken cancellationToken = default);

    /// <summary>How many active employees are currently assigned to each department - one query for any number of departments.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByDepartmentAsync(IEnumerable<Guid> departmentIds, CancellationToken cancellationToken = default);
}

/// <summary>Someone joined. Recruitment, onboarding, payroll and resourcing react to this.</summary>
[MessageType("gggs.employee.hired")]
public sealed record EmployeeHiredIntegrationEvent(
    Guid EmployeeId,
    string EmployeeCode,
    DateOnly JoiningDate,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ManagerEmployeeId) : IntegrationEvent;

/// <summary>An employee's assignment changed: transfer, promotion, new manager, confirmation.</summary>
[MessageType("gggs.employee.assignment-changed")]
public sealed record EmployeeAssignmentChangedIntegrationEvent(
    Guid EmployeeId,
    string ChangeType,
    DateOnly EffectiveFrom,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? GradeId,
    Guid? ManagerEmployeeId) : IntegrationEvent;

/// <summary>An employee's employment ended.</summary>
[MessageType("gggs.employee.exited")]
public sealed record EmployeeExitedIntegrationEvent(Guid EmployeeId, DateOnly ExitDate, string ExitReasonCode) : IntegrationEvent;
