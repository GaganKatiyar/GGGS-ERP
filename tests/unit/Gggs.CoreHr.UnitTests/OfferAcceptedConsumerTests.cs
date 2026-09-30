using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;
using Gggs.Talent.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>
/// OfferAcceptedConsumer's own job: resolve the legal entity from the event's branch id (the event
/// itself does not carry one - see <see cref="OfferAcceptedIntegrationEvent"/>'s own remarks) and
/// call <see cref="EmployeeService.HireAsync"/> with it. The redelivery/inbox-dedup story is the
/// framework's job, not this class's - see <c>AuditEntryConsumerTests</c>'s own remarks on the same
/// division for a different consumer.
/// </summary>
public sealed class OfferAcceptedConsumerTests
{
    private static readonly DateOnly JoiningDate = new(2026, 4, 1);

    private readonly LegalEntity _legalEntity = LegalEntity.Create("LE1", "Acme India", "IN", "INR");
    private readonly Designation _designation = Designation.Create("ENG1", "Engineer");
    private readonly Department _department = Department.Create("ENG", "Engineering");
    private readonly Branch _branch;

    public OfferAcceptedConsumerTests()
    {
        _branch = Branch.Create("HQ", "Head Office", _legalEntity.Id, "IN", "Asia/Kolkata");
    }

    [Fact]
    public async Task HandlingAnAcceptedOffer_HiresAnEmployeeIntoTheBranchsLegalEntity()
    {
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        IOrganizationRepository organization = Substitute.For<IOrganizationRepository>();
        organization.GetBranchAsync(_branch.Id, Arg.Any<CancellationToken>()).Returns(_branch);
        organization.GetLegalEntityAsync(_legalEntity.Id, Arg.Any<CancellationToken>()).Returns(_legalEntity);
        organization.GetDepartmentAsync(_department.Id, Arg.Any<CancellationToken>()).Returns(_department);
        organization.GetDesignationAsync(_designation.Id, Arg.Any<CancellationToken>()).Returns(_designation);

        EmployeeService employeeService = new(
            employees, organization, Substitute.For<IIntegrationEventPublisher>(),
            Substitute.For<IUnitOfWork>(), NullLogger<EmployeeService>.Instance);

        var consumer = new OfferAcceptedConsumer(employeeService, organization, NullLogger<OfferAcceptedConsumer>.Instance);

        var integrationEvent = new OfferAcceptedIntegrationEvent(
            OfferId: Guid.NewGuid(),
            ApplicationId: Guid.NewGuid(),
            CandidateId: Guid.NewGuid(),
            CandidateFirstName: "Priya",
            CandidateLastName: "Sharma",
            CandidateEmail: "priya.sharma@example.com",
            BranchId: _branch.Id,
            DepartmentId: _department.Id,
            DesignationId: _designation.Id,
            GradeId: null,
            JoiningDate: JoiningDate);

        await consumer.HandleAsync(integrationEvent, TestContext.Current.CancellationToken);

        employees.Received(1).Add(Arg.Is<Employee>(e =>
            e.FirstName == "Priya" && e.LastName == "Sharma" && e.WorkEmail == "priya.sharma@example.com"));

        employees.Received(1).Add(Arg.Is<EmploymentRecord>(r =>
            r.LegalEntityId == _legalEntity.Id
            && r.BranchId == _branch.Id
            && r.DepartmentId == _department.Id
            && r.DesignationId == _designation.Id
            && r.IsCurrent));
    }

    [Fact]
    public async Task HandlingAnAcceptedOffer_ForABranchThatNoLongerExists_ThrowsRatherThanHiringIntoNothing()
    {
        IEmployeeRepository employees = Substitute.For<IEmployeeRepository>();
        IOrganizationRepository organization = Substitute.For<IOrganizationRepository>();
        organization.GetBranchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Branch?)null);

        EmployeeService employeeService = new(
            employees, organization, Substitute.For<IIntegrationEventPublisher>(),
            Substitute.For<IUnitOfWork>(), NullLogger<EmployeeService>.Instance);

        var consumer = new OfferAcceptedConsumer(employeeService, organization, NullLogger<OfferAcceptedConsumer>.Instance);

        var integrationEvent = new OfferAcceptedIntegrationEvent(
            OfferId: Guid.NewGuid(),
            ApplicationId: Guid.NewGuid(),
            CandidateId: Guid.NewGuid(),
            CandidateFirstName: "Priya",
            CandidateLastName: "Sharma",
            CandidateEmail: "priya.sharma@example.com",
            BranchId: Guid.NewGuid(),
            DepartmentId: _department.Id,
            DesignationId: _designation.Id,
            GradeId: null,
            JoiningDate: JoiningDate);

        await Should.ThrowAsync<InvalidOperationException>(
            () => consumer.HandleAsync(integrationEvent, TestContext.Current.CancellationToken));

        employees.DidNotReceive().Add(Arg.Any<Employee>());
    }
}
