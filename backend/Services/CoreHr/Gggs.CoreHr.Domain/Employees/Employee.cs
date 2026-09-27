using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.CoreHr.Domain.Employees.Events;

namespace Gggs.CoreHr.Domain.Employees;

public enum EmployeeStatus
{
    PreBoarding = 1,
    Probation = 2,
    Confirmed = 3,
    OnNotice = 4,
    Exited = 5,
    Absconded = 6,
}

/// <summary>
/// The core record of a person the tenant employs.
/// <para>
/// Deliberately thin. Everything that changes over time - department, designation, manager - lives
/// in <see cref="EmploymentRecord"/>, not here; everything genuinely sensitive - bank details,
/// national identifiers, date of birth - is left out of this pass entirely rather than added in
/// plain text, because the architecture requires those fields encrypted at rest and that
/// infrastructure does not exist yet. Adding them unencrypted would be worse than not having them.
/// </para>
/// <para>
/// Not the same as a <c>User</c>. An employee with no system access has no user at all, and
/// <see cref="UserId"/> is null until Identity issues one - onboarding a person and giving them a
/// login are two different moments, and often two different people's job to do.
/// </para>
/// </summary>
public sealed class Employee : TenantAggregateRoot
{
    private Employee()
    {
        EmployeeCode = null!;
        FirstName = null!;
        DisplayName = null!;
    }

    public string EmployeeCode { get; private set; }

    public string FirstName { get; private set; }

    public string? MiddleName { get; private set; }

    public string? LastName { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>The address this person is reached at for work. Not encrypted: it is, deliberately, as public as a business card.</summary>
    public string? WorkEmail { get; private set; }

    public EmployeeStatus Status { get; private set; }

    public DateOnly DateOfJoining { get; private set; }

    /// <summary>Set only on a rehire, so a former employee's original tenure is not lost when they return.</summary>
    public DateOnly? OriginalHireDate { get; private set; }

    public DateOnly? ConfirmationDate { get; private set; }

    public DateOnly? ExitDate { get; private set; }

    /// <summary>Linked once Identity issues this person an account. Null for someone with no system access at all.</summary>
    public Guid? UserId { get; private set; }

    public static Employee Hire(
        string employeeCode,
        string firstName,
        string? middleName,
        string? lastName,
        string? workEmail,
        DateOnly dateOfJoining)
    {
        var employee = new Employee
        {
            EmployeeCode = EmployeeCodeFormat.Normalize(employeeCode),
            FirstName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(firstName).Trim(), 100),
            MiddleName = NormalizeNamePart(middleName),
            LastName = NormalizeNamePart(lastName),
            WorkEmail = NormalizeWorkEmail(workEmail),
            Status = EmployeeStatus.PreBoarding,
            DateOfJoining = dateOfJoining,
        };

        employee.DisplayName = ComposeDisplayName(employee.FirstName, employee.MiddleName, employee.LastName);

        employee.Raise(new EmployeeHiredDomainEvent(employee.Id, employee.EmployeeCode, employee.DisplayName, dateOfJoining));
        return employee;
    }

    public void UpdateName(string firstName, string? middleName, string? lastName)
    {
        FirstName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(firstName).Trim(), 100);
        MiddleName = NormalizeNamePart(middleName);
        LastName = NormalizeNamePart(lastName);
        DisplayName = ComposeDisplayName(FirstName, MiddleName, LastName);
    }

    public void SetWorkEmail(string? workEmail) => WorkEmail = NormalizeWorkEmail(workEmail);

    public void LinkUser(Guid userId) => UserId = Guard.NotEmpty(userId);

    /// <summary>
    /// Starts working, for someone hired with a start date still ahead of them. <see cref="Confirm"/>
    /// is what actually moves a probation employee to confirmed; this only covers the
    /// pre-boarding-to-probation step, which every new hire passes through.
    /// </summary>
    public void BeginProbation()
    {
        Guard.Against(
            Status != EmployeeStatus.PreBoarding,
            "EMPLOYEE_NOT_PRE_BOARDING",
            $"Only a pre-boarding employee can begin probation; this one is {Status}.");

        Status = EmployeeStatus.Probation;
    }

    public void Confirm(DateOnly confirmationDate)
    {
        Guard.Against(
            Status is not (EmployeeStatus.PreBoarding or EmployeeStatus.Probation),
            "EMPLOYEE_NOT_ON_PROBATION",
            $"Only a probationary employee can be confirmed; this one is {Status}.");

        Guard.Against(
            confirmationDate < DateOfJoining,
            "EMPLOYEE_CONFIRMATION_BEFORE_JOINING",
            "An employee cannot be confirmed before they joined.");

        Status = EmployeeStatus.Confirmed;
        ConfirmationDate = confirmationDate;

        Raise(new EmployeeConfirmedDomainEvent(Id, confirmationDate));
    }

    public void ServeNotice()
    {
        Guard.Against(
            Status is EmployeeStatus.Exited or EmployeeStatus.Absconded,
            "EMPLOYEE_ALREADY_LEFT",
            $"This employee has already left ({Status}).");

        Status = EmployeeStatus.OnNotice;
    }

    /// <summary>
    /// Ends employment. <paramref name="wasAbsconded"/> is a separate flag rather than a change to
    /// the meaning of "exited": full and final settlement, exit interviews and rehire eligibility
    /// all treat the two very differently, and collapsing them into one status would lose the
    /// distinction the moment it happened.
    /// </summary>
    public void Exit(DateOnly exitDate, bool wasAbsconded = false)
    {
        Guard.Against(
            Status is EmployeeStatus.Exited or EmployeeStatus.Absconded,
            "EMPLOYEE_ALREADY_LEFT",
            $"This employee has already left ({Status}).");

        Guard.Against(
            exitDate < DateOfJoining,
            "EMPLOYEE_EXIT_BEFORE_JOINING",
            "An employee cannot exit before they joined.");

        Status = wasAbsconded ? EmployeeStatus.Absconded : EmployeeStatus.Exited;
        ExitDate = exitDate;

        Raise(new EmployeeExitedDomainEvent(Id, exitDate, wasAbsconded ? "ABSCONDED" : "EXITED"));
    }

    /// <summary>
    /// Brings back a former employee. Their original hire date is preserved separately from the new
    /// <see cref="DateOfJoining"/>, so tenure calculations and "years of service" can still answer
    /// honestly across the gap - a rehire's second stint does not erase their first one.
    /// </summary>
    public void Rehire(DateOnly newDateOfJoining)
    {
        Guard.Against(
            Status is not (EmployeeStatus.Exited or EmployeeStatus.Absconded),
            "EMPLOYEE_NOT_EXITED",
            "Only a former employee can be rehired.");

        OriginalHireDate ??= DateOfJoining;
        DateOfJoining = newDateOfJoining;
        ExitDate = null;
        ConfirmationDate = null;
        Status = EmployeeStatus.PreBoarding;
    }

    private static string? NormalizeNamePart(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), 100);

    private static string? NormalizeWorkEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        Guard.Against(
            !trimmed.Contains('@', StringComparison.Ordinal) || trimmed.Length > 256,
            "EMPLOYEE_WORK_EMAIL_INVALID",
            "A valid email address is required.");

        return trimmed;
    }

    private static string ComposeDisplayName(string firstName, string? middleName, string? lastName)
        => string.Join(' ', new[] { firstName, middleName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

/// <summary>
/// Employee code normalization - the same shape as <c>OrganizationCode</c>, kept separate rather
/// than shared: an employee code is often driven by a tenant's own numbering scheme (<c>cfg.NumberSequence</c>,
/// not yet built) rather than typed by hand, and the two are conceptually different identifiers
/// that happen to look alike today.
/// <para>
/// Named <c>EmployeeCodeFormat</c> rather than <c>EmployeeCode</c> - the more obvious name - because
/// <see cref="Employee"/> already has an instance property of that name, and inside the class the
/// property wins: a call meant for this static class would silently try to resolve the property
/// instead and fail to compile from a completely unrelated-looking error.
/// </para>
/// </summary>
public static class EmployeeCodeFormat
{
    public const int MaxLength = 30;

    public static string Normalize(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), MaxLength);
}
