using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Employee.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Application;

public sealed record AddressInput(string? Line1, string? Line2, string? City, string? StateCode, string? CountryCode, string? PostalCode);

/// <summary>An assignment as a caller describes it. A position, when given, fills in its job, department, grade and location.</summary>
public sealed record AssignmentInput(
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? GradeId,
    Guid? LocationId,
    Guid? LegalEntityId,
    Guid? BusinessUnitId,
    Guid? DivisionId,
    Guid? ManagerEmployeeId,
    Guid? HrbpEmployeeId,
    string EmploymentTypeCode,
    string WorkModeCode,
    string? Designation);

public sealed record HireEmployeeRequest(
    string? EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PreferredName,
    string? GenderCode,
    DateOnly? DateOfBirth,
    string? PersonalEmail,
    string? OfficialEmail,
    string? MobilePhone,
    DateOnly JoiningDate,
    DateOnly? ProbationEndDate,
    AssignmentInput Assignment);

public sealed record UpdatePersonalRequest(
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PreferredName,
    string? GenderCode,
    DateOnly? DateOfBirth,
    string? MaritalStatusCode,
    string? NationalityCode,
    string? BloodGroupCode);

public sealed record UpdateContactRequest(
    string? PersonalEmail,
    string? OfficialEmail,
    string? MobilePhone,
    string? AlternatePhone,
    AddressInput? CurrentAddress,
    AddressInput? PermanentAddress);

/// <summary>Statutory identifiers in the clear, on the way in only. Null leaves a value as it is; an empty string clears it.</summary>
public sealed record UpdateStatutoryRequest(string? Pan, string? Uan, string? EsicNumber, string? NationalId, string? ProfessionalTaxStateCode);

public sealed record UpdateBankRequest(string? BankName, string? Ifsc, string? AccountHolder, string? AccountNumber);

public sealed record ChangeAssignmentRequest(AssignmentChangeType ChangeType, DateOnly EffectiveFrom, AssignmentInput Assignment, string? Reason);

public sealed record ConfirmRequest(DateOnly ConfirmationDate);

public sealed record ContractEndRequest(DateOnly? ContractEndDate);

public sealed record StartNoticeRequest(DateOnly NoticeStartDate);

public sealed record ExitRequest(DateOnly ExitDate, string ExitReasonCode);

public sealed record AddDependentRequest(string FullName, string RelationshipCode, DateOnly? DateOfBirth, string? GenderCode);

public sealed record AddEmergencyContactRequest(string FullName, string RelationshipCode, string Phone, string? Email, bool IsPrimary);

/// <summary>
/// The Core HR rules. Every write that touches another module's data goes through that module's
/// contract (organization references, lookup codes), and every change other modules care about is
/// published in the same transaction as the change itself.
/// </summary>
public sealed class EmployeeService(
    AppDbContext dbContext,
    IUnitOfWork unitOfWork,
    IOrgStructure orgStructure,
    IMasterDataLookup lookups,
    ISensitiveDataProtector protector,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<EmployeeEntity> HireAsync(HireEmployeeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = string.IsNullOrWhiteSpace(request.EmployeeCode)
            ? await NextEmployeeCodeAsync(cancellationToken)
            : EmployeeEntity.NormalizeCode(request.EmployeeCode);

        if (await dbContext.Employees.AnyAsync(employee => employee.EmployeeCode == code, cancellationToken))
        {
            throw new DomainRuleException("EMPLOYEE_CODE_IN_USE", $"The employee code '{code}' is already in use.");
        }

        Assignment assignment = await ResolveAssignmentAsync(request.Assignment, employeeId: null, cancellationToken);

        EmployeeEntity employee = EmployeeEntity.Hire(
            code, request.FirstName, request.MiddleName, request.LastName, request.JoiningDate, request.ProbationEndDate, assignment);

        await EnsureLookupAsync(LookupLists.Gender, request.GenderCode, "gender", cancellationToken);
        employee.UpdatePersonal(
            request.FirstName, request.MiddleName, request.LastName, request.PreferredName, request.GenderCode, request.DateOfBirth,
            maritalStatusCode: null, nationalityCode: null, bloodGroupCode: null, Today);

        await EnsureEmailFreeAsync(request.OfficialEmail, employeeId: null, cancellationToken);
        employee.UpdateContact(request.PersonalEmail, request.OfficialEmail, request.MobilePhone, null, null, null);

        dbContext.Employees.Add(employee);
        publisher.Publish(new EmployeeHiredIntegrationEvent(
            employee.Id, employee.EmployeeCode, employee.JoiningDate, employee.DepartmentId, employee.PositionId, employee.ManagerEmployeeId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task UpdatePersonalAsync(Guid employeeId, UpdatePersonalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);

        await EnsureLookupAsync(LookupLists.Gender, request.GenderCode, "gender", cancellationToken);
        await EnsureLookupAsync(LookupLists.MaritalStatus, request.MaritalStatusCode, "marital status", cancellationToken);
        await EnsureLookupAsync(LookupLists.Country, request.NationalityCode, "nationality", cancellationToken);
        await EnsureLookupAsync(LookupLists.BloodGroup, request.BloodGroupCode, "blood group", cancellationToken);

        employee.UpdatePersonal(
            request.FirstName, request.MiddleName, request.LastName, request.PreferredName, request.GenderCode, request.DateOfBirth,
            request.MaritalStatusCode, request.NationalityCode, request.BloodGroupCode, Today);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateContactAsync(Guid employeeId, UpdateContactRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);

        await EnsureEmailFreeAsync(request.OfficialEmail, employeeId, cancellationToken);
        await EnsureAddressAsync(request.CurrentAddress, cancellationToken);
        await EnsureAddressAsync(request.PermanentAddress, cancellationToken);

        employee.UpdateContact(
            request.PersonalEmail, request.OfficialEmail, request.MobilePhone, request.AlternatePhone,
            ToAddress(request.CurrentAddress), ToAddress(request.PermanentAddress));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatutoryAsync(Guid employeeId, UpdateStatutoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);

        await EnsureLookupAsync(LookupLists.State, request.ProfessionalTaxStateCode, "professional tax state", cancellationToken);

        employee.SetStatutory(
            Protect(SensitiveDataPurposes.Pan, request.Pan, employee.Pan, StatutoryFormats.Pan, "PAN"),
            Protect(SensitiveDataPurposes.Uan, request.Uan, employee.Uan, StatutoryFormats.Uan, "UAN"),
            Protect(SensitiveDataPurposes.Esic, request.EsicNumber, employee.EsicNumber, StatutoryFormats.Esic, "ESIC number"),
            Protect(SensitiveDataPurposes.NationalId, request.NationalId, employee.NationalId, validate: null, "national ID"),
            request.ProfessionalTaxStateCode ?? employee.ProfessionalTaxStateCode);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateBankAsync(Guid employeeId, UpdateBankRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Ifsc) && !StatutoryFormats.Ifsc(request.Ifsc.Trim().ToUpperInvariant()))
        {
            throw new DomainRuleException("EMPLOYEE_IFSC_INVALID", "That is not a valid IFSC code (four letters, a zero, then six letters or digits).");
        }

        employee.SetBank(
            request.BankName,
            request.Ifsc,
            request.AccountHolder,
            Protect(SensitiveDataPurposes.BankAccount, request.AccountNumber, employee.BankAccountNumber, StatutoryFormats.BankAccount, "account number"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmploymentRecord> ChangeAssignmentAsync(Guid employeeId, ChangeAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken, includeRecords: true);

        Assignment assignment = await ResolveAssignmentAsync(request.Assignment, employeeId, cancellationToken);
        EmploymentRecord record = employee.ChangeAssignment(request.ChangeType, request.EffectiveFrom, assignment, request.Reason, Today);

        // Added explicitly: keys are generated client-side, so EF would otherwise take a new record
        // reached through a tracked parent's collection for an existing row and try to update it.
        dbContext.EmploymentRecords.Add(record);

        publisher.Publish(new EmployeeAssignmentChangedIntegrationEvent(
            employee.Id, request.ChangeType.ToString(), record.EffectiveFrom, record.DepartmentId, record.PositionId, record.JobId, record.GradeId, record.ManagerEmployeeId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task ConfirmAsync(Guid employeeId, ConfirmRequest request, CancellationToken cancellationToken = default)
    {
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);
        employee.Confirm(request.ConfirmationDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetContractEndAsync(Guid employeeId, ContractEndRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);
        employee.SetContractEnd(request.ContractEndDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task StartNoticeAsync(Guid employeeId, StartNoticeRequest request, CancellationToken cancellationToken = default)
    {
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);
        employee.StartNotice(request.NoticeStartDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task WithdrawNoticeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);
        employee.WithdrawNotice();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ExitAsync(Guid employeeId, ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken, includeRecords: true);

        await EnsureLookupAsync(LookupLists.ExitReason, request.ExitReasonCode, "exit reason", cancellationToken, required: true);

        if (await dbContext.Employees.AnyAsync(report => report.ManagerEmployeeId == employeeId && report.Status != EmployeeStatus.Exited, cancellationToken))
        {
            throw new DomainRuleException("EMPLOYEE_HAS_REPORTS", "Move this employee's direct reports to another manager before they exit.");
        }

        employee.Exit(request.ExitDate, request.ExitReasonCode);
        publisher.Publish(new EmployeeExitedIntegrationEvent(employee.Id, request.ExitDate, employee.ExitReasonCode!));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmployeeDependent> AddDependentAsync(Guid employeeId, AddDependentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await LoadAsync(employeeId, cancellationToken);

        await EnsureLookupAsync(LookupLists.Relationship, request.RelationshipCode, "relationship", cancellationToken, required: true);
        await EnsureLookupAsync(LookupLists.Gender, request.GenderCode, "gender", cancellationToken);

        EmployeeDependent dependent = employee.AddDependent(request.FullName, request.RelationshipCode, request.DateOfBirth, request.GenderCode);
        dbContext.EmployeeDependents.Add(dependent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dependent;
    }

    public async Task RemoveDependentAsync(Guid employeeId, Guid dependentId, CancellationToken cancellationToken = default)
    {
        EmployeeDependent dependent = await dbContext.EmployeeDependents
            .FirstOrDefaultAsync(candidate => candidate.Id == dependentId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeDependent), dependentId);

        dbContext.EmployeeDependents.Remove(dependent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmergencyContact> AddEmergencyContactAsync(Guid employeeId, AddEmergencyContactRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmployeeEntity employee = await dbContext.Employees
            .Include(candidate => candidate.EmergencyContacts)
            .FirstOrDefaultAsync(candidate => candidate.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

        await EnsureLookupAsync(LookupLists.Relationship, request.RelationshipCode, "relationship", cancellationToken, required: true);

        EmergencyContact contact = employee.AddEmergencyContact(request.FullName, request.RelationshipCode, request.Phone, request.Email, request.IsPrimary);
        dbContext.EmergencyContacts.Add(contact);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public async Task RemoveEmergencyContactAsync(Guid employeeId, Guid contactId, CancellationToken cancellationToken = default)
    {
        EmergencyContact contact = await dbContext.EmergencyContacts
            .FirstOrDefaultAsync(candidate => candidate.Id == contactId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmergencyContact), contactId);

        dbContext.EmergencyContacts.Remove(contact);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Validates an assignment and turns it into the domain's form. A position, when given, decides
    /// the job and department (and supplies grade and location unless the caller set them), must
    /// have a free seat, and must agree with anything the caller set explicitly.
    /// </summary>
    internal async Task<Assignment> ResolveAssignmentAsync(AssignmentInput input, Guid? employeeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        Guid? departmentId = input.DepartmentId;
        Guid? jobId = input.JobId;
        Guid? gradeId = input.GradeId;
        Guid? locationId = input.LocationId;

        if (input.PositionId is { } positionId)
        {
            PositionPlacement position = await orgStructure.GetPositionAsync(positionId, cancellationToken)
                ?? throw Invalid("position", "The selected position does not exist or is closed.");

            if ((departmentId is not null && departmentId != position.DepartmentId) || (jobId is not null && jobId != position.JobId))
            {
                throw new DomainRuleException("EMPLOYEE_POSITION_MISMATCH", "The department and job must match the selected position's.");
            }

            departmentId = position.DepartmentId;
            jobId = position.JobId;
            gradeId ??= position.GradeId;
            locationId ??= position.LocationId;

            int occupied = await dbContext.Employees.CountAsync(
                employee => employee.PositionId == positionId && employee.Status != EmployeeStatus.Exited && employee.Id != employeeId,
                cancellationToken);

            if (occupied >= position.Headcount)
            {
                throw new DomainRuleException("EMPLOYEE_POSITION_FULL", "The selected position has no free seat.");
            }
        }

        if (departmentId is null)
        {
            throw Invalid("department", "A department (or a position) is required.");
        }

        List<OrgReference> references = [new(OrgUnitKind.Department, departmentId.Value)];
        Add(references, OrgUnitKind.Job, jobId);
        Add(references, OrgUnitKind.Grade, gradeId);
        Add(references, OrgUnitKind.Location, locationId);
        Add(references, OrgUnitKind.LegalEntity, input.LegalEntityId);
        Add(references, OrgUnitKind.BusinessUnit, input.BusinessUnitId);
        Add(references, OrgUnitKind.Division, input.DivisionId);

        IReadOnlyList<OrgReference> invalid = await orgStructure.FindInvalidAsync(references, cancellationToken);
        if (invalid.Count > 0)
        {
            string field = invalid[0].Kind.ToString();
            throw Invalid(field, $"The selected {field} does not exist or is inactive.");
        }

        await EnsureLookupAsync(LookupLists.EmploymentType, input.EmploymentTypeCode, "employment type", cancellationToken, required: true);
        await EnsureLookupAsync(LookupLists.WorkMode, input.WorkModeCode, "work mode", cancellationToken, required: true);
        await EnsureManagerAsync(input.ManagerEmployeeId, employeeId, "manager", cancellationToken);
        await EnsureManagerAsync(input.HrbpEmployeeId, employeeId, "HR business partner", cancellationToken, checkCycle: false);

        return new Assignment(
            input.LegalEntityId, input.BusinessUnitId, input.DivisionId, departmentId.Value, jobId, input.PositionId, gradeId, locationId,
            input.ManagerEmployeeId, input.HrbpEmployeeId, input.EmploymentTypeCode, input.WorkModeCode, input.Designation);

        static void Add(List<OrgReference> list, OrgUnitKind kind, Guid? id)
        {
            if (id is not null)
            {
                list.Add(new OrgReference(kind, id.Value));
            }
        }
    }

    /// <summary>
    /// The manager must be an active employee, and - when the employee already exists - must not
    /// report to them anywhere up the line, or the reporting line would loop.
    /// </summary>
    private async Task EnsureManagerAsync(Guid? managerId, Guid? employeeId, string field, CancellationToken cancellationToken, bool checkCycle = true)
    {
        if (managerId is null)
        {
            return;
        }

        if (managerId == employeeId)
        {
            throw Invalid(field, $"An employee cannot be their own {field}.");
        }

        if (!await dbContext.Employees.AnyAsync(employee => employee.Id == managerId && employee.Status != EmployeeStatus.Exited, cancellationToken))
        {
            throw Invalid(field, $"The selected {field} is not an active employee.");
        }

        if (!checkCycle || employeeId is null)
        {
            return;
        }

        Dictionary<Guid, Guid?> managers = await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.ManagerEmployeeId != null)
            .ToDictionaryAsync(employee => employee.Id, employee => employee.ManagerEmployeeId, cancellationToken);

        Guid? current = managerId;
        var seen = new HashSet<Guid>();
        while (current is { } step && seen.Add(step))
        {
            if (step == employeeId)
            {
                throw new DomainRuleException("EMPLOYEE_MANAGER_CYCLE", "That manager reports to this employee, so the reporting line would loop.");
            }

            current = managers.GetValueOrDefault(step);
        }
    }

    private async Task<EmployeeEntity> LoadAsync(Guid employeeId, CancellationToken cancellationToken, bool includeRecords = false)
    {
        IQueryable<EmployeeEntity> query = dbContext.Employees;
        if (includeRecords)
        {
            query = query.Include(employee => employee.EmploymentRecords.Where(record => record.EffectiveTo == null));
        }

        return await query.FirstOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);
    }

    /// <summary>EMP00001, EMP00002, …: the next number after the highest code already in that shape.</summary>
    private async Task<string> NextEmployeeCodeAsync(CancellationToken cancellationToken)
    {
        const string prefix = "EMP";

        List<string> codes = await dbContext.Employees
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Where(employee => employee.EmployeeCode.StartsWith(prefix))
            .Select(employee => employee.EmployeeCode)
            .ToListAsync(cancellationToken);

        int highest = codes
            .Select(code => int.TryParse(code.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D5}";
    }

    private async Task EnsureEmailFreeAsync(string? officialEmail, Guid? employeeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(officialEmail))
        {
            return;
        }

        string normalized = officialEmail.Trim().ToLowerInvariant();
        if (await dbContext.Employees.AnyAsync(employee => employee.OfficialEmail == normalized && employee.Id != employeeId, cancellationToken))
        {
            throw new DomainRuleException("EMPLOYEE_EMAIL_IN_USE", "Another employee already has that official email address.");
        }
    }

    private async Task EnsureAddressAsync(AddressInput? address, CancellationToken cancellationToken)
    {
        await EnsureLookupAsync(LookupLists.Country, address?.CountryCode, "country", cancellationToken);
        await EnsureLookupAsync(LookupLists.State, address?.StateCode, "state", cancellationToken);
    }

    private async Task EnsureLookupAsync(string listCode, string? code, string field, CancellationToken cancellationToken, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            if (required)
            {
                throw Invalid(field, $"A {field} is required.");
            }

            return;
        }

        if (!await lookups.IsValidAsync(listCode, code, cancellationToken))
        {
            throw Invalid(field, $"'{code}' is not a valid {field}.");
        }
    }

    /// <summary>
    /// Null keeps the stored value; an empty string clears it; anything else is validated and
    /// encrypted. Plain values never reach the domain or the database.
    /// </summary>
    private ProtectedValue? Protect(string purpose, string? plain, ProtectedValue? existing, Func<string, bool>? validate, string field)
    {
        if (plain is null)
        {
            return existing;
        }

        if (plain.Length == 0 || string.IsNullOrWhiteSpace(plain))
        {
            return null;
        }

        string normalized = plain.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (validate is not null && !validate(normalized))
        {
            throw Invalid(field, $"That is not a valid {field}.");
        }

        return protector.Protect(purpose, normalized);
    }

    private static HomeAddress? ToAddress(AddressInput? input)
        => input is null || (input.Line1, input.Line2, input.City, input.StateCode, input.CountryCode, input.PostalCode) is (null, null, null, null, null, null)
            ? null
            : new HomeAddress(
                input.Line1?.Trim(), input.Line2?.Trim(), input.City?.Trim(),
                input.StateCode?.Trim().ToUpperInvariant(), input.CountryCode?.Trim().ToUpperInvariant(), input.PostalCode?.Trim());

    private static DomainRuleException Invalid(string field, string message)
        => new("EMPLOYEE_FIELD_INVALID", message) { Context = new Dictionary<string, object?> { ["field"] = field } };
}

/// <summary>Format checks for Indian statutory identifiers (spec §22), applied before encryption.</summary>
internal static class StatutoryFormats
{
    /// <summary>PAN: five letters, four digits, one letter - ABCDE1234F.</summary>
    public static bool Pan(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z]{5}[0-9]{4}[A-Z]$");

    /// <summary>UAN: twelve digits.</summary>
    public static bool Uan(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{12}$");

    /// <summary>ESIC insurance number: seventeen digits (ten for older cards).</summary>
    public static bool Esic(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^([0-9]{10}|[0-9]{17})$");

    /// <summary>IFSC: four letters, a zero, then six letters or digits.</summary>
    public static bool Ifsc(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z]{4}0[A-Z0-9]{6}$");

    /// <summary>Bank account numbers: nine to eighteen digits.</summary>
    public static bool BankAccount(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{9,18}$");
}
