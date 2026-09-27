using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>
/// EmployeeService's own job: check every organizational reference exists before an aggregate is
/// touched, and keep the "exactly one current employment record" invariant intact across transfers,
/// including when the change itself turns out to be invalid.
/// <para>
/// Exercised against fakes, not a database: the interesting behaviour is which repository calls
/// happen in which order, not what SQL Server does with the result.
/// </para>
/// </summary>
public sealed class EmployeeServiceTests
{
    private static readonly DateOnly DateOfJoining = new(2026, 1, 15);

    private readonly LegalEntity _legalEntity = LegalEntity.Create("LE1", "Acme India", "IN", "INR");
    private readonly Designation _designation = Designation.Create("ENG1", "Engineer");
    private readonly Department _department = Department.Create("ENG", "Engineering");
    private readonly Branch _branch;

    public EmployeeServiceTests()
    {
        _branch = Branch.Create("HQ", "Head Office", _legalEntity.Id, "IN", "Asia/Kolkata");
    }

    [Fact]
    public async Task HiringWithACodeAlreadyInUse_IsRejectedBeforeTouchingTheAggregate()
    {
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        employees.EmployeeCodeExistsAsync("EMP001", Arg.Any<CancellationToken>()).Returns(true);
        EmployeeService service = BuildService(employees, FakeOrganizationWithAllReferences());

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.HireAsync("EMP001", "Priya", null, "Sharma", null, DateOfJoining, ValidAssignment()));

        exception.ErrorCode.ShouldBe("EMPLOYEE_CODE_IN_USE");
        employees.DidNotReceive().Add(Arg.Any<Employee>());
    }

    [Fact]
    public async Task HiringIntoADepartmentThatDoesNotExist_IsRejectedWithNotFound()
    {
        // Every org reference is checked before any aggregate is touched, so a typo'd id produces a
        // clean 404 instead of a database constraint violation surfacing as a raw 500.
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        IOrganizationRepository organization = FakeOrganizationWithAllReferences();
        organization.GetDepartmentAsync(_department.Id, Arg.Any<CancellationToken>()).Returns((Department?)null);
        EmployeeService service = BuildService(employees, organization);

        await Should.ThrowAsync<NotFoundException>(
            () => service.HireAsync("EMP001", "Priya", null, "Sharma", null, DateOfJoining, ValidAssignment()));

        employees.DidNotReceive().Add(Arg.Any<Employee>());
    }

    [Fact]
    public async Task HiringWithAReportingManagerThatDoesNotExist_IsRejected()
    {
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        Guid managerId = Guid.NewGuid();
        employees.GetAsync(managerId, Arg.Any<CancellationToken>()).Returns((Employee?)null);
        EmployeeService service = BuildService(employees, FakeOrganizationWithAllReferences());

        await Should.ThrowAsync<NotFoundException>(
            () => service.HireAsync("EMP001", "Priya", null, "Sharma", null, DateOfJoining, ValidAssignment(reportingManagerId: managerId)));
    }

    [Fact]
    public async Task HiringWithEverythingValid_AddsBothTheEmployeeAndAnOpenEmploymentRecord()
    {
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        EmployeeService service = BuildService(employees, FakeOrganizationWithAllReferences());

        Employee employee = await service.HireAsync(
            "EMP001", "Priya", null, "Sharma", null, DateOfJoining, ValidAssignment(), TestContext.Current.CancellationToken);

        employees.Received(1).Add(employee);
        employees.Received(1).Add(Arg.Is<EmploymentRecord>(r => r.EmployeeId == employee.Id && r.IsCurrent));
    }

    [Fact]
    public async Task TransferringToAnInvalidAssignment_LeavesTheOriginalRecordCurrent()
    {
        // ChangeAssignmentAsync builds the successor record before ending the current one, precisely
        // so a rejected change never leaves the employee with no current record - this proves that
        // ordering rather than just asserting the error code.
        Employee employee = Employee.Hire("EMP001", "Priya", null, "Sharma", null, DateOfJoining);
        EmploymentRecord current = EmploymentRecord.Create(
            employee.Id, DateOfJoining, _legalEntity.Id, _branch.Id, _department.Id, _designation.Id,
            null, null, null, EmploymentType.Permanent, EmploymentChangeType.Hire, "Hired");

        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        employees.GetAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        employees.GetCurrentEmploymentRecordAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(current);

        EmployeeService service = BuildService(employees, FakeOrganizationWithAllReferences());

        // Reporting to yourself is a guard EmploymentRecord.Create raises, not something the service
        // pre-checks - exactly the kind of late failure this ordering has to survive.
        await Should.ThrowAsync<DomainRuleException>(
            () => service.TransferAsync(employee.Id, DateOfJoining.AddMonths(1), ValidAssignment(reportingManagerId: employee.Id), "Self-transfer attempt"));

        current.IsCurrent.ShouldBeTrue();
        current.EffectiveTo.ShouldBeNull();
        employees.DidNotReceive().Add(Arg.Is<EmploymentRecord>(r => r != current));
    }

    [Fact]
    public async Task TransferringSuccessfully_EndsTheCurrentRecordTheDayBeforeTheNewOneStarts()
    {
        Employee employee = Employee.Hire("EMP001", "Priya", null, "Sharma", null, DateOfJoining);
        EmploymentRecord current = EmploymentRecord.Create(
            employee.Id, DateOfJoining, _legalEntity.Id, _branch.Id, _department.Id, _designation.Id,
            null, null, null, EmploymentType.Permanent, EmploymentChangeType.Hire, "Hired");

        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        employees.GetAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        employees.GetCurrentEmploymentRecordAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(current);

        EmployeeService service = BuildService(employees, FakeOrganizationWithAllReferences());
        DateOnly transferDate = DateOfJoining.AddMonths(3);

        EmploymentRecord next = await service.TransferAsync(employee.Id, transferDate, ValidAssignment(), "Reorg", TestContext.Current.CancellationToken);

        current.IsCurrent.ShouldBeFalse();
        current.EffectiveTo.ShouldBe(transferDate.AddDays(-1));
        next.IsCurrent.ShouldBeTrue();
        next.ChangeType.ShouldBe(EmploymentChangeType.Transfer);
        employees.Received(1).Add(next);
    }

    private static EmployeeService BuildService(IEmployeeRepository employees, IOrganizationRepository organization)
        => new(employees, organization, Substitute.For<IUnitOfWork>(), NullLogger<EmployeeService>.Instance);

    private AssignmentDetails ValidAssignment(Guid? reportingManagerId = null)
        => new(_legalEntity.Id, _branch.Id, _department.Id, _designation.Id, null, null, reportingManagerId, EmploymentType.Permanent);

    private IOrganizationRepository FakeOrganizationWithAllReferences()
    {
        IOrganizationRepository organization = Substitute.For<IOrganizationRepository>();
        organization.GetLegalEntityAsync(_legalEntity.Id, Arg.Any<CancellationToken>()).Returns(_legalEntity);
        organization.GetBranchAsync(_branch.Id, Arg.Any<CancellationToken>()).Returns(_branch);
        organization.GetDepartmentAsync(_department.Id, Arg.Any<CancellationToken>()).Returns(_department);
        organization.GetDesignationAsync(_designation.Id, Arg.Any<CancellationToken>()).Returns(_designation);
        return organization;
    }
}
