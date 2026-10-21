using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Employee.Domain;

public enum EmployeeStatus
{
    /// <summary>Joined and working; may still be on probation (see <see cref="Employee.IsConfirmed"/>).</summary>
    Active = 1,

    /// <summary>Has resigned or been given notice, and is still working it out.</summary>
    OnNotice = 2,

    /// <summary>Employment has ended. The record stays: payroll, history and alumni need it.</summary>
    Exited = 3,
}

/// <summary>Why an employment record exists: what changed on that date.</summary>
public enum AssignmentChangeType
{
    Hire = 1,
    Transfer = 2,
    Promotion = 3,
    ManagerChange = 4,
    Redesignation = 5,
    EmploymentTermsChange = 6,
    Rehire = 7,
}

/// <summary>A home address. Country and state are lookup codes.</summary>
public sealed record HomeAddress(string? Line1, string? Line2, string? City, string? StateCode, string? CountryCode, string? PostalCode);

/// <summary>
/// Where someone sits and on what terms, as of one date: the content of an employment record.
/// Organization ids point into the Organization module; codes are MasterData lookups.
/// </summary>
public sealed record Assignment(
    Guid? LegalEntityId,
    Guid? BusinessUnitId,
    Guid? DivisionId,
    Guid DepartmentId,
    Guid? JobId,
    Guid? PositionId,
    Guid? GradeId,
    Guid? LocationId,
    Guid? ManagerEmployeeId,
    Guid? HrbpEmployeeId,
    string EmploymentTypeCode,
    string WorkModeCode,
    string? Designation);

/// <summary>
/// One effective-dated slice of an employee's employment (spec §14): from this date, this
/// assignment. Records are never edited after the fact - a change closes the current record the day
/// before and opens a new one - so the history answers "where did they sit on 3 March?" exactly.
/// </summary>
public sealed class EmploymentRecord : TenantAggregateRoot
{
    private EmploymentRecord()
    {
        EmploymentTypeCode = null!;
        WorkModeCode = null!;
    }

    public Guid EmployeeId { get; private set; }

    public AssignmentChangeType ChangeType { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public Guid? LegalEntityId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public Guid? DivisionId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? JobId { get; private set; }

    public Guid? PositionId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? LocationId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public Guid? HrbpEmployeeId { get; private set; }

    public string EmploymentTypeCode { get; private set; }

    public string WorkModeCode { get; private set; }

    public string? Designation { get; private set; }

    public string? Reason { get; private set; }

    internal static EmploymentRecord Open(Guid employeeId, AssignmentChangeType changeType, DateOnly effectiveFrom, Assignment assignment, string? reason) => new()
    {
        EmployeeId = employeeId,
        ChangeType = changeType,
        EffectiveFrom = effectiveFrom,
        LegalEntityId = assignment.LegalEntityId,
        BusinessUnitId = assignment.BusinessUnitId,
        DivisionId = assignment.DivisionId,
        DepartmentId = Guard.NotEmpty(assignment.DepartmentId),
        JobId = assignment.JobId,
        PositionId = assignment.PositionId,
        GradeId = assignment.GradeId,
        LocationId = assignment.LocationId,
        ManagerEmployeeId = assignment.ManagerEmployeeId,
        HrbpEmployeeId = assignment.HrbpEmployeeId,
        EmploymentTypeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(assignment.EmploymentTypeCode).Trim().ToUpperInvariant(), 64),
        WorkModeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(assignment.WorkModeCode).Trim().ToUpperInvariant(), 64),
        Designation = string.IsNullOrWhiteSpace(assignment.Designation) ? null : Guard.MaxLength(assignment.Designation.Trim(), 200),
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500),
    };

    internal void CloseOn(DateOnly lastDay) => EffectiveTo = lastDay;

    public Assignment ToAssignment() => new(
        LegalEntityId, BusinessUnitId, DivisionId, DepartmentId, JobId, PositionId, GradeId, LocationId,
        ManagerEmployeeId, HrbpEmployeeId, EmploymentTypeCode, WorkModeCode, Designation);
}

/// <summary>Someone the employee supports or insures: a spouse, a child (benefits enrolment uses these).</summary>
public sealed class EmployeeDependent : TenantAggregateRoot
{
    private EmployeeDependent()
    {
        FullName = null!;
        RelationshipCode = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string FullName { get; private set; }

    public string RelationshipCode { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public string? GenderCode { get; private set; }

    internal static EmployeeDependent Create(Guid employeeId, string fullName, string relationshipCode, DateOnly? dateOfBirth, string? genderCode) => new()
    {
        EmployeeId = employeeId,
        FullName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fullName).Trim(), 200),
        RelationshipCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(relationshipCode).Trim().ToUpperInvariant(), 64),
        DateOfBirth = dateOfBirth,
        GenderCode = string.IsNullOrWhiteSpace(genderCode) ? null : genderCode.Trim().ToUpperInvariant(),
    };
}

/// <summary>Who to call when something happens to the employee at work.</summary>
public sealed class EmergencyContact : TenantAggregateRoot
{
    private EmergencyContact()
    {
        FullName = null!;
        RelationshipCode = null!;
        Phone = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string FullName { get; private set; }

    public string RelationshipCode { get; private set; }

    public string Phone { get; private set; }

    public string? Email { get; private set; }

    public bool IsPrimary { get; private set; }

    internal static EmergencyContact Create(Guid employeeId, string fullName, string relationshipCode, string phone, string? email, bool isPrimary) => new()
    {
        EmployeeId = employeeId,
        FullName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fullName).Trim(), 200),
        RelationshipCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(relationshipCode).Trim().ToUpperInvariant(), 64),
        Phone = Guard.MaxLength(Guard.NotNullOrWhiteSpace(phone).Trim(), 32),
        Email = string.IsNullOrWhiteSpace(email) ? null : Guard.MaxLength(email.Trim(), 256),
        IsPrimary = isPrimary,
    };

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

/// <summary>
/// An employee (spec §14): who they are, how to reach them, their statutory and bank details, and -
/// through <see cref="EmploymentRecord"/>s - where they have sat over time.
/// <para>
/// Not one giant table (spec §15): employment history, dependents and emergency contacts are their
/// own rows, and everything other modules know about an employee (leave, payroll, projects) lives in
/// those modules, keyed by this id.
/// </para>
/// <para>
/// The current assignment is copied onto the employee row when it changes, so directory queries and
/// other modules can filter by department or manager without reading history. Changes take effect
/// on their effective date, which may be in the past but not the future (future-dated changes need a
/// scheduled activation and arrive with the workflow engine in M3).
/// </para>
/// </summary>
public sealed class Employee : TenantAggregateRoot
{
    private readonly List<EmploymentRecord> _employmentRecords = [];
    private readonly List<EmployeeDependent> _dependents = [];
    private readonly List<EmergencyContact> _emergencyContacts = [];

    private Employee()
    {
        EmployeeCode = null!;
        FirstName = null!;
        DisplayName = null!;
        EmploymentTypeCode = null!;
        WorkModeCode = null!;
    }

    public string EmployeeCode { get; private set; }

    // ----- Personal -----

    public string FirstName { get; private set; }

    public string? MiddleName { get; private set; }

    public string? LastName { get; private set; }

    public string? PreferredName { get; private set; }

    /// <summary>The name shown everywhere: preferred name (or first name) and last name.</summary>
    public string DisplayName { get; private set; }

    public string? GenderCode { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public string? MaritalStatusCode { get; private set; }

    public string? NationalityCode { get; private set; }

    public string? BloodGroupCode { get; private set; }

    /// <summary>A document in the Documents module (M3).</summary>
    public Guid? PhotoDocumentId { get; private set; }

    // ----- Contact -----

    public string? PersonalEmail { get; private set; }

    public string? OfficialEmail { get; private set; }

    public string? MobilePhone { get; private set; }

    public string? AlternatePhone { get; private set; }

    public HomeAddress? CurrentAddress { get; private set; }

    public HomeAddress? PermanentAddress { get; private set; }

    // ----- Employment -----

    public EmployeeStatus Status { get; private set; }

    public DateOnly JoiningDate { get; private set; }

    public DateOnly? ProbationEndDate { get; private set; }

    public DateOnly? ConfirmationDate { get; private set; }

    public DateOnly? NoticeStartDate { get; private set; }

    public DateOnly? ExitDate { get; private set; }

    public string? ExitReasonCode { get; private set; }

    /// <summary>When a fixed-term contract ends; Separation proposes the exit ahead of it.</summary>
    public DateOnly? ContractEndDate { get; private set; }

    /// <summary>The Identity user who signs in as this employee, once linked.</summary>
    public Guid? UserId { get; private set; }

    // Current assignment, copied from the open employment record.
    public Guid? LegalEntityId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public Guid? DivisionId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? JobId { get; private set; }

    public Guid? PositionId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? LocationId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public Guid? HrbpEmployeeId { get; private set; }

    public string EmploymentTypeCode { get; private set; }

    public string WorkModeCode { get; private set; }

    public string? Designation { get; private set; }

    // ----- Statutory and bank (encrypted; see ProtectedValue) -----

    public ProtectedValue? Pan { get; private set; }

    public ProtectedValue? Uan { get; private set; }

    public ProtectedValue? EsicNumber { get; private set; }

    public ProtectedValue? NationalId { get; private set; }

    /// <summary>The state whose professional tax applies (a <c>state</c> lookup code).</summary>
    public string? ProfessionalTaxStateCode { get; private set; }

    public string? BankName { get; private set; }

    public string? BankIfsc { get; private set; }

    public string? BankAccountHolder { get; private set; }

    public ProtectedValue? BankAccountNumber { get; private set; }

    public IReadOnlyList<EmploymentRecord> EmploymentRecords => _employmentRecords;

    public IReadOnlyList<EmployeeDependent> Dependents => _dependents;

    public IReadOnlyList<EmergencyContact> EmergencyContacts => _emergencyContacts;

    public bool IsConfirmed => ConfirmationDate is not null;

    public EmploymentRecord? CurrentRecord => _employmentRecords.FirstOrDefault(record => record.EffectiveTo is null);

    public static Employee Hire(
        string employeeCode,
        string firstName,
        string? middleName,
        string? lastName,
        DateOnly joiningDate,
        DateOnly? probationEndDate,
        Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Guard.Against(
            probationEndDate is not null && probationEndDate < joiningDate,
            "EMPLOYEE_PROBATION_BEFORE_JOINING",
            "Probation cannot end before the joining date.");

        var employee = new Employee
        {
            EmployeeCode = NormalizeCode(employeeCode),
            Status = EmployeeStatus.Active,
            JoiningDate = joiningDate,
            ProbationEndDate = probationEndDate,
        };

        employee.SetNames(firstName, middleName, lastName, preferredName: null);
        employee.Open(AssignmentChangeType.Hire, joiningDate, assignment, reason: null);
        return employee;
    }

    public static string NormalizeCode(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32);

    public void UpdatePersonal(
        string firstName,
        string? middleName,
        string? lastName,
        string? preferredName,
        string? genderCode,
        DateOnly? dateOfBirth,
        string? maritalStatusCode,
        string? nationalityCode,
        string? bloodGroupCode,
        DateOnly today)
    {
        Guard.Against(dateOfBirth is not null && dateOfBirth > today, "EMPLOYEE_BIRTH_IN_FUTURE", "Date of birth cannot be in the future.");
        Guard.Against(
            dateOfBirth is not null && dateOfBirth > JoiningDate.AddYears(-14),
            "EMPLOYEE_TOO_YOUNG",
            "The date of birth makes the employee younger than 14 on the joining date.");

        SetNames(firstName, middleName, lastName, preferredName);
        GenderCode = Code(genderCode);
        DateOfBirth = dateOfBirth;
        MaritalStatusCode = Code(maritalStatusCode);
        NationalityCode = Code(nationalityCode);
        BloodGroupCode = Code(bloodGroupCode);
    }

    public void UpdateContact(
        string? personalEmail,
        string? officialEmail,
        string? mobilePhone,
        string? alternatePhone,
        HomeAddress? currentAddress,
        HomeAddress? permanentAddress)
    {
        PersonalEmail = Email(personalEmail);
        OfficialEmail = Email(officialEmail);
        MobilePhone = Text(mobilePhone, 32);
        AlternatePhone = Text(alternatePhone, 32);
        CurrentAddress = currentAddress;
        PermanentAddress = permanentAddress;
    }

    public void SetStatutory(ProtectedValue? pan, ProtectedValue? uan, ProtectedValue? esicNumber, ProtectedValue? nationalId, string? professionalTaxStateCode)
    {
        Pan = pan;
        Uan = uan;
        EsicNumber = esicNumber;
        NationalId = nationalId;
        ProfessionalTaxStateCode = Code(professionalTaxStateCode);
    }

    public void SetBank(string? bankName, string? ifsc, string? accountHolder, ProtectedValue? accountNumber)
    {
        Guard.Against(
            accountNumber is not null && string.IsNullOrWhiteSpace(ifsc),
            "EMPLOYEE_BANK_IFSC_REQUIRED",
            "A bank account needs the branch IFSC code.");

        BankName = Text(bankName, 200);
        BankIfsc = string.IsNullOrWhiteSpace(ifsc) ? null : Guard.MaxLength(ifsc.Trim().ToUpperInvariant(), 11);
        BankAccountHolder = Text(accountHolder, 200);
        BankAccountNumber = accountNumber;
    }

    /// <summary>
    /// Records a change of assignment on <paramref name="effectiveFrom"/>: the open record closes the
    /// day before and a new one opens. Back-dating is allowed as far as the day after the open
    /// record began; future dates are not (see the class remarks).
    /// </summary>
    public EmploymentRecord ChangeAssignment(AssignmentChangeType changeType, DateOnly effectiveFrom, Assignment assignment, string? reason, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        EnsureNotExited();
        Guard.Against(changeType is AssignmentChangeType.Hire or AssignmentChangeType.Rehire, "EMPLOYEE_CHANGE_TYPE_INVALID", "Use hire or rehire for those changes.");
        Guard.Against(effectiveFrom > today, "EMPLOYEE_CHANGE_IN_FUTURE", "Future-dated changes are not supported yet; use today's date or earlier.");

        EmploymentRecord current = CurrentRecord ?? throw new InvalidOperationException("An active employee always has an open employment record.");
        Guard.Against(
            effectiveFrom <= current.EffectiveFrom,
            "EMPLOYEE_CHANGE_BEFORE_CURRENT",
            $"The change must take effect after {current.EffectiveFrom:yyyy-MM-dd}, when the current assignment began.");
        Guard.Against(assignment.ManagerEmployeeId == Id, "EMPLOYEE_OWN_MANAGER", "An employee cannot be their own manager.");

        current.CloseOn(effectiveFrom.AddDays(-1));
        return Open(changeType, effectiveFrom, assignment, reason);
    }

    public void SetContractEnd(DateOnly? contractEndDate)
    {
        EnsureNotExited();
        Guard.Against(contractEndDate < JoiningDate, "EMPLOYEE_CONTRACT_END", "A contract cannot end before the joining date.");
        ContractEndDate = contractEndDate;
    }

    public void Confirm(DateOnly confirmationDate)
    {
        EnsureNotExited();
        Guard.Against(IsConfirmed, "EMPLOYEE_ALREADY_CONFIRMED", "This employee is already confirmed.");
        Guard.Against(confirmationDate < JoiningDate, "EMPLOYEE_CONFIRMATION_BEFORE_JOINING", "Confirmation cannot precede the joining date.");
        ConfirmationDate = confirmationDate;
    }

    /// <summary>Notice has been given (by either side); the employee keeps working until the exit date.</summary>
    public void StartNotice(DateOnly noticeStartDate)
    {
        EnsureNotExited();
        Guard.Against(noticeStartDate < JoiningDate, "EMPLOYEE_NOTICE_BEFORE_JOINING", "Notice cannot start before the joining date.");
        Status = EmployeeStatus.OnNotice;
        NoticeStartDate = noticeStartDate;
    }

    /// <summary>A resignation was withdrawn: back to working normally.</summary>
    public void WithdrawNotice()
    {
        EnsureNotExited();
        Guard.Against(Status != EmployeeStatus.OnNotice, "EMPLOYEE_NOT_ON_NOTICE", "This employee is not serving notice.");
        Status = EmployeeStatus.Active;
        NoticeStartDate = null;
    }

    /// <summary>
    /// Ends employment on <paramref name="exitDate"/> (the last working day). The open record closes
    /// on that date. Clearance and full-and-final settlement belong to the Separation module (M10).
    /// </summary>
    public void Exit(DateOnly exitDate, string exitReasonCode)
    {
        EnsureNotExited();
        EmploymentRecord current = CurrentRecord ?? throw new InvalidOperationException("An active employee always has an open employment record.");
        Guard.Against(exitDate < current.EffectiveFrom, "EMPLOYEE_EXIT_BEFORE_CURRENT", "The exit date cannot precede the current assignment.");

        current.CloseOn(exitDate);
        Status = EmployeeStatus.Exited;
        ExitDate = exitDate;
        ExitReasonCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(exitReasonCode).Trim().ToUpperInvariant(), 64);
        IsActive = false;
    }

    public void LinkUser(Guid userId) => UserId = Guard.NotEmpty(userId);

    public EmployeeDependent AddDependent(string fullName, string relationshipCode, DateOnly? dateOfBirth, string? genderCode)
    {
        EmployeeDependent dependent = EmployeeDependent.Create(Id, fullName, relationshipCode, dateOfBirth, genderCode);
        _dependents.Add(dependent);
        return dependent;
    }

    public EmergencyContact AddEmergencyContact(string fullName, string relationshipCode, string phone, string? email, bool isPrimary)
    {
        // One primary contact: naming a new one demotes the old.
        bool primary = isPrimary || _emergencyContacts.Count == 0;
        if (primary)
        {
            foreach (EmergencyContact existing in _emergencyContacts)
            {
                existing.SetPrimary(false);
            }
        }

        EmergencyContact contact = EmergencyContact.Create(Id, fullName, relationshipCode, phone, email, primary);
        _emergencyContacts.Add(contact);
        return contact;
    }

    private EmploymentRecord Open(AssignmentChangeType changeType, DateOnly effectiveFrom, Assignment assignment, string? reason)
    {
        EmploymentRecord record = EmploymentRecord.Open(Id, changeType, effectiveFrom, assignment, reason);
        _employmentRecords.Add(record);

        LegalEntityId = record.LegalEntityId;
        BusinessUnitId = record.BusinessUnitId;
        DivisionId = record.DivisionId;
        DepartmentId = record.DepartmentId;
        JobId = record.JobId;
        PositionId = record.PositionId;
        GradeId = record.GradeId;
        LocationId = record.LocationId;
        ManagerEmployeeId = record.ManagerEmployeeId;
        HrbpEmployeeId = record.HrbpEmployeeId;
        EmploymentTypeCode = record.EmploymentTypeCode;
        WorkModeCode = record.WorkModeCode;
        Designation = record.Designation;
        return record;
    }

    private void SetNames(string firstName, string? middleName, string? lastName, string? preferredName)
    {
        FirstName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(firstName).Trim(), 100);
        MiddleName = Text(middleName, 100);
        LastName = Text(lastName, 100);
        PreferredName = Text(preferredName, 100);
        DisplayName = Guard.MaxLength(string.Join(' ', new[] { PreferredName ?? FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part))), 200);
    }

    private void EnsureNotExited()
        => Guard.Against(Status == EmployeeStatus.Exited, "EMPLOYEE_EXITED", "This employee has exited; rehire them first.");

    private static string? Text(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);

    private static string? Code(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim().ToUpperInvariant(), 64);

    private static string? Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = Guard.MaxLength(value.Trim().ToLowerInvariant(), 256);
        Guard.Against(!trimmed.Contains('@', StringComparison.Ordinal) || trimmed.StartsWith('@') || trimmed.EndsWith('@'), "EMPLOYEE_EMAIL_INVALID", $"'{value}' is not an email address.");
        return trimmed;
    }
}
