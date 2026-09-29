using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Contracts;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging;

namespace Gggs.CoreHr.Application.Employees;

/// <summary>What the caller supplies for a new organizational assignment, shared by hiring and every later change.</summary>
public sealed record AssignmentDetails(
    Guid LegalEntityId,
    Guid BranchId,
    Guid DepartmentId,
    Guid DesignationId,
    Guid? GradeId,
    Guid? CostCenterId,
    Guid? ReportingManagerId,
    EmploymentType EmploymentType,
    DateOnly? ProbationEndDate = null);

/// <summary>
/// Hiring, moving and exiting employees.
/// <para>
/// Every organizational reference here - legal entity, branch, department, designation, grade, cost
/// center, reporting manager - is checked before the aggregates are touched, and checked the same
/// way <see cref="OrganizationService"/> checks a code's uniqueness: a hire that half-succeeds
/// because a typo'd department id was only discovered by the database is a worse failure mode than
/// a clean 404 before anything is written.
/// </para>
/// </summary>
public sealed class EmployeeService(
    IEmployeeRepository employees,
    IOrganizationRepository organization,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    ILogger<EmployeeService> logger)
{
    public async Task<Employee> HireAsync(
        string employeeCode,
        string firstName,
        string? middleName,
        string? lastName,
        string? workEmail,
        DateOnly dateOfJoining,
        AssignmentDetails assignment,
        CancellationToken cancellationToken = default)
    {
        string normalizedCode = EmployeeCodeFormat.Normalize(employeeCode);

        if (await employees.EmployeeCodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new DomainRuleException("EMPLOYEE_CODE_IN_USE", $"An employee with code '{normalizedCode}' already exists.");
        }

        await EnsureAssignmentReferencesExistAsync(assignment, cancellationToken);

        Employee employee = Employee.Hire(employeeCode, firstName, middleName, lastName, workEmail, dateOfJoining);
        employees.Add(employee);

        EmploymentRecord record = EmploymentRecord.Create(
            employee.Id,
            dateOfJoining,
            assignment.LegalEntityId,
            assignment.BranchId,
            assignment.DepartmentId,
            assignment.DesignationId,
            assignment.GradeId,
            assignment.CostCenterId,
            assignment.ReportingManagerId,
            assignment.EmploymentType,
            EmploymentChangeType.Hire,
            "Hired",
            assignment.ProbationEndDate);

        employees.Add(record);

        publisher.Publish(BuildAssignmentChangedEvent(employee, record, version: 1));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Employee {EmployeeId} ({Code}) hired.", employee.Id, employee.EmployeeCode);
        return employee;
    }

    /// <summary>A change of department, branch or legal entity - most of what "transfer" means day to day.</summary>
    public Task<EmploymentRecord> TransferAsync(
        Guid employeeId,
        DateOnly effectiveFrom,
        AssignmentDetails assignment,
        string? reason,
        CancellationToken cancellationToken = default)
        => ChangeAssignmentAsync(employeeId, effectiveFrom, assignment, EmploymentChangeType.Transfer, reason, cancellationToken);

    /// <summary>A change of grade or designation without necessarily changing where someone sits.</summary>
    public Task<EmploymentRecord> PromoteAsync(
        Guid employeeId,
        DateOnly effectiveFrom,
        AssignmentDetails assignment,
        string? reason,
        CancellationToken cancellationToken = default)
        => ChangeAssignmentAsync(employeeId, effectiveFrom, assignment, EmploymentChangeType.Promotion, reason, cancellationToken);

    /// <summary>A change of reporting manager alone, everything else carried forward unchanged.</summary>
    public async Task<EmploymentRecord> ChangeReportingManagerAsync(
        Guid employeeId,
        DateOnly effectiveFrom,
        Guid? newReportingManagerId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        EmploymentRecord current = await employees.GetCurrentEmploymentRecordAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmploymentRecord), employeeId);

        var assignment = new AssignmentDetails(
            current.LegalEntityId,
            current.BranchId,
            current.DepartmentId,
            current.DesignationId,
            current.GradeId,
            current.CostCenterId,
            newReportingManagerId,
            current.EmploymentType,
            current.ProbationEndDate);

        return await ChangeAssignmentAsync(employeeId, effectiveFrom, assignment, EmploymentChangeType.ManagerChange, reason, cancellationToken);
    }

    private async Task<EmploymentRecord> ChangeAssignmentAsync(
        Guid employeeId,
        DateOnly effectiveFrom,
        AssignmentDetails assignment,
        EmploymentChangeType changeType,
        string? reason,
        CancellationToken cancellationToken)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        EmploymentRecord current = await employees.GetCurrentEmploymentRecordAsync(employeeId, cancellationToken)
            ?? throw new InvalidOperationException($"Employee {employeeId} has no current employment record.");

        await EnsureAssignmentReferencesExistAsync(assignment, cancellationToken);

        EmploymentRecord next = EmploymentRecord.Create(
            employee.Id,
            effectiveFrom,
            assignment.LegalEntityId,
            assignment.BranchId,
            assignment.DepartmentId,
            assignment.DesignationId,
            assignment.GradeId,
            assignment.CostCenterId,
            assignment.ReportingManagerId,
            assignment.EmploymentType,
            changeType,
            reason,
            assignment.ProbationEndDate);

        // Ended after the new record is built, not before: EmploymentRecord.Create's own guards
        // (no reporting to yourself, real ids) run first, so a rejected change never leaves the
        // employee with no current record at all.
        current.EndOn(effectiveFrom);
        employees.Add(next);

        int historyCount = (await employees.GetEmploymentHistoryAsync(employeeId, cancellationToken)).Count;
        publisher.Publish(BuildAssignmentChangedEvent(employee, next, version: historyCount + 1));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Employee {EmployeeId} {ChangeType} effective {EffectiveFrom}.",
            employeeId,
            changeType,
            effectiveFrom);

        return next;
    }

    public async Task<Employee> UpdateNameAsync(
        Guid employeeId,
        string firstName,
        string? middleName,
        string? lastName,
        CancellationToken cancellationToken = default)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        employee.UpdateName(firstName, middleName, lastName);
        await PublishSnapshotRefreshAsync(employee, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task<Employee> ConfirmAsync(Guid employeeId, DateOnly confirmationDate, CancellationToken cancellationToken = default)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        employee.Confirm(confirmationDate);
        await PublishSnapshotRefreshAsync(employee, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task<Employee> ServeNoticeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        employee.ServeNotice();
        await PublishSnapshotRefreshAsync(employee, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return employee;
    }

    /// <summary>
    /// Brings a former employee back with a fresh assignment. Unlike <see cref="HireAsync"/>, no
    /// employee code check runs: this is the same person's row, not a new one, and the point of
    /// <c>Employee.Rehire</c> is exactly that their history is not started over.
    /// </summary>
    public async Task<Employee> RehireAsync(
        Guid employeeId,
        DateOnly newDateOfJoining,
        AssignmentDetails assignment,
        CancellationToken cancellationToken = default)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        await EnsureAssignmentReferencesExistAsync(assignment, cancellationToken);

        employee.Rehire(newDateOfJoining);

        EmploymentRecord record = EmploymentRecord.Create(
            employee.Id,
            newDateOfJoining,
            assignment.LegalEntityId,
            assignment.BranchId,
            assignment.DepartmentId,
            assignment.DesignationId,
            assignment.GradeId,
            assignment.CostCenterId,
            assignment.ReportingManagerId,
            assignment.EmploymentType,
            EmploymentChangeType.Rehire,
            "Rehired",
            assignment.ProbationEndDate);

        employees.Add(record);

        int historyCount = (await employees.GetEmploymentHistoryAsync(employeeId, cancellationToken)).Count;
        publisher.Publish(BuildAssignmentChangedEvent(employee, record, version: historyCount + 1));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Employee {EmployeeId} rehired effective {DateOfJoining}.", employeeId, newDateOfJoining);
        return employee;
    }

    public async Task<Employee> ExitAsync(
        Guid employeeId,
        DateOnly exitDate,
        bool wasAbsconded,
        CancellationToken cancellationToken = default)
    {
        Employee employee = await employees.GetAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        employee.Exit(exitDate, wasAbsconded);

        // The employee's last employment record does not end itself: it stays open, describing
        // what their assignment was right up to departure, which is what a full-and-final
        // settlement or an exit report actually needs to read.
        int historyCount = (await employees.GetEmploymentHistoryAsync(employeeId, cancellationToken)).Count;
        publisher.Publish(new EmployeeExitedIntegrationEvent(employeeId, historyCount));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return employee;
    }

    private async Task EnsureAssignmentReferencesExistAsync(AssignmentDetails assignment, CancellationToken cancellationToken)
    {
        _ = await organization.GetLegalEntityAsync(assignment.LegalEntityId, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalEntity), assignment.LegalEntityId);

        _ = await organization.GetBranchAsync(assignment.BranchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Branch), assignment.BranchId);

        _ = await organization.GetDepartmentAsync(assignment.DepartmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), assignment.DepartmentId);

        _ = await organization.GetDesignationAsync(assignment.DesignationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Designation), assignment.DesignationId);

        if (assignment.GradeId is Guid gradeId)
        {
            _ = await organization.GetGradeAsync(gradeId, cancellationToken)
                ?? throw new NotFoundException(nameof(Grade), gradeId);
        }

        if (assignment.CostCenterId is Guid costCenterId)
        {
            _ = await organization.GetCostCenterAsync(costCenterId, cancellationToken)
                ?? throw new NotFoundException(nameof(CostCenter), costCenterId);
        }

        if (assignment.ReportingManagerId is Guid managerId)
        {
            _ = await employees.GetAsync(managerId, cancellationToken)
                ?? throw new NotFoundException(nameof(Employee), managerId);
        }
    }

    /// <summary>
    /// Republishes the snapshot for a change that touches <see cref="Employee"/> alone (name,
    /// status) rather than producing a new <see cref="EmploymentRecord"/> - the org-reference
    /// fields are unchanged, but consumers watching <see cref="EmployeeAssignmentChangedIntegrationEvent.Status"/>
    /// (Workflow's SoD checks, an approver who just went on notice) still need to hear about it.
    /// </summary>
    private async Task PublishSnapshotRefreshAsync(Employee employee, CancellationToken cancellationToken)
    {
        EmploymentRecord? current = await employees.GetCurrentEmploymentRecordAsync(employee.Id, cancellationToken);

        if (current is null)
        {
            return;
        }

        int historyCount = (await employees.GetEmploymentHistoryAsync(employee.Id, cancellationToken)).Count;
        publisher.Publish(BuildAssignmentChangedEvent(employee, current, version: historyCount));
    }

    private static EmployeeAssignmentChangedIntegrationEvent BuildAssignmentChangedEvent(
        Employee employee, EmploymentRecord record, int version) => new(
            employee.Id,
            employee.EmployeeCode,
            employee.DisplayName,
            employee.Status.ToString(),
            employee.UserId,
            record.LegalEntityId,
            record.BranchId,
            record.DepartmentId,
            record.DesignationId,
            record.GradeId,
            record.ReportingManagerId,
            employee.DateOfJoining,
            version);
}
