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
    Guid? LegalEntityId,
    Guid? HrbpEmployeeId = null,
    string? Designation = null,
    DateOnly? JoiningDate = null,
    string? EmploymentTypeCode = null,
    DateOnly? ProbationEndDate = null,
    DateOnly? ConfirmationDate = null,
    string? WorkModeCode = null,
    Guid? GradeId = null,
    DateOnly? ExitDate = null,
    string? ExitReasonCode = null);

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

    /// <summary>
    /// Active employees, optionally only those in the given departments or locations (either
    /// matching is enough). Both null means everyone - a policy for the whole company.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListActiveIdsAsync(
        IReadOnlyCollection<Guid>? departmentIds,
        IReadOnlyCollection<Guid>? locationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller's grant of <paramref name="permission"/> reaches this employee - the same
    /// data-scope rule Core HR applies to its own lists, for modules that hold records about people
    /// (documents, approvals) and must not show them more widely than the person themselves.
    /// </summary>
    Task<bool> IsInScopeAsync(Guid employeeId, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// The employees the caller's grant of <paramref name="permission"/> reaches, or null when it
    /// reaches everyone (tenant scope) - so a list can filter by the set without loading it whole.
    /// </summary>
    Task<IReadOnlyCollection<Guid>?> InScopeIdsAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>Ids for employee codes (case-insensitive), for imports that name people by code. Unknown codes are left out.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ResolveCodesAsync(IEnumerable<string> employeeCodes, CancellationToken cancellationToken = default);
}

/// <summary>
/// The statutory and bank details payroll works with. Masked unless the caller asks for them
/// revealed - for a bank file or a statutory return - and the caller is responsible for having
/// checked a permission that justifies seeing them.
/// </summary>
public sealed record PayrollIdentity(
    Guid EmployeeId,
    string? Pan,
    string? Uan,
    string? EsicNumber,
    string? ProfessionalTaxStateCode,
    string? BankName,
    string? BankIfsc,
    string? BankAccountHolder,
    string? BankAccountNumber,
    DateOnly? DateOfBirth,
    string? GenderCode,
    bool Revealed);

/// <summary>A family member recorded against an employee, for benefit enrollment.</summary>
public sealed record DependentInfo(Guid Id, Guid EmployeeId, string FullName, string RelationshipCode, DateOnly? DateOfBirth);

/// <summary>Employee details for payroll and benefits (ADR-12).</summary>
public interface IEmployeePayrollDetails
{
    Task<IReadOnlyDictionary<Guid, PayrollIdentity>> GetAsync(IEnumerable<Guid> employeeIds, bool reveal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DependentInfo>> DependentsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>A new hire as recruitment hands them over: the person, the date they join and where they sit.</summary>
public sealed record NewHire(
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PersonalEmail,
    string? MobilePhone,
    DateOnly JoiningDate,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? GradeId,
    Guid? LocationId,
    Guid? ManagerEmployeeId,
    string EmploymentTypeCode,
    string WorkModeCode,
    string? Designation);

/// <summary>Creating an employee from outside Core HR (ADR-12): recruitment hires accepted candidates through it.</summary>
public interface IEmployeeHiring
{
    /// <summary>Hires the person with Core HR's own rules and events. Returns the new employee's id and code.</summary>
    Task<(Guid EmployeeId, string EmployeeCode)> HireAsync(NewHire hire, CancellationToken cancellationToken = default);
}

/// <summary>A promotion decided elsewhere (Career): the new job, grade, position or designation from a day. Unset values stay as they are.</summary>
public sealed record Promotion(Guid EmployeeId, DateOnly EffectiveFrom, Guid? PositionId, Guid? JobId, Guid? GradeId, string? Designation, string Reason);

/// <summary>Applying a promotion from outside Core HR (ADR-12), through Core HR's own assignment rules and events.</summary>
public interface IEmployeePromotions
{
    Task PromoteAsync(Promotion promotion, CancellationToken cancellationToken = default);
}

/// <summary>How to reach someone outside work.</summary>
public sealed record PersonalContact(string? PersonalEmail, string? MobilePhone);

/// <summary>An exit that falls due on its own: superannuation or the end of a fixed-term contract.</summary>
public sealed record UpcomingExit(Guid EmployeeId, UpcomingExitKind Kind, DateOnly Date);

public enum UpcomingExitKind
{
    Retirement = 1,
    ContractEnd = 2,
}

/// <summary>Notice and exit decided in Separation (ADR-12), applied through Core HR's own rules and events.</summary>
public interface IEmployeeSeparations
{
    Task StartNoticeAsync(Guid employeeId, DateOnly noticeStartDate, CancellationToken cancellationToken = default);

    Task WithdrawNoticeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Ends employment on the last working day; Core HR publishes the exit event.</summary>
    Task ExitAsync(Guid employeeId, DateOnly exitDate, string exitReasonCode, CancellationToken cancellationToken = default);

    /// <summary>Current employees who report to <paramref name="managerEmployeeId"/>.</summary>
    Task<IReadOnlyList<Guid>> DirectReportsAsync(Guid managerEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Current employees who reach the retirement age (on the last day of that month) or the end of a
    /// fixed-term contract on or before <paramref name="until"/>.
    /// </summary>
    Task<IReadOnlyList<UpcomingExit>> UpcomingExitsAsync(int retirementAge, DateOnly until, CancellationToken cancellationToken = default);

    /// <summary>Personal email and mobile, kept with the alumni record to recognise a former employee who applies again.</summary>
    Task<IReadOnlyDictionary<Guid, PersonalContact>> PersonalContactsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Moves an employee to a new manager (a leaver's handover), as a manager-change assignment.</summary>
    Task ChangeManagerAsync(Guid employeeId, Guid newManagerEmployeeId, DateOnly effectiveFrom, string reason, CancellationToken cancellationToken = default);
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
