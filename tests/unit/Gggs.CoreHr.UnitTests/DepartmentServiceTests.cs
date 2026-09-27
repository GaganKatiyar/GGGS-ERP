using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>
/// The one rule <see cref="DepartmentService.MoveAsync"/> exists to enforce: a move that would make
/// a department its own descendant's descendant.
/// <para>
/// Exercised against a fake repository rather than a database, because the tree it needs to walk is
/// exactly the input the test controls - three or four departments wired together by hand say more
/// about whether the walk is correct than seeding a real one would.
/// </para>
/// </summary>
public sealed class DepartmentServiceTests
{
    [Fact]
    public async Task MovingADepartmentUnderItself_IsRefused()
    {
        Department department = Department.Create("ENG", "Engineering");
        IOrganizationRepository repository = FakeRepositoryReturning(department);

        DepartmentService service = BuildService(repository);

        // The service's own ancestor walk (a proposed parent that is the department itself is a
        // cycle of length one) catches this before Department.MoveUnder's narrower guard ever runs,
        // so the code returned is the cycle code, not the aggregate's own-parent code.
        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.MoveAsync(department.Id, department.Id));

        exception.ErrorCode.ShouldBe("DEPARTMENT_MOVE_WOULD_CREATE_CYCLE");
    }

    [Fact]
    public async Task MovingADepartmentUnderItsOwnGrandchild_IsRefused()
    {
        // Engineering -> Platform -> Core. Moving Engineering under Core would make Engineering a
        // descendant of its own descendant - the cycle a single node cannot see on its own, which is
        // exactly why this walk exists.
        Department engineering = Department.Create("ENG", "Engineering");
        Department platform = Department.Create("PLATFORM", "Platform", engineering.Id);
        Department core = Department.Create("CORE", "Core", platform.Id);

        IOrganizationRepository repository = FakeRepositoryReturning(engineering, platform, core);
        DepartmentService service = BuildService(repository);

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.MoveAsync(engineering.Id, core.Id));

        exception.ErrorCode.ShouldBe("DEPARTMENT_MOVE_WOULD_CREATE_CYCLE");
    }

    [Fact]
    public async Task MovingADepartmentUnderAnUnrelatedDepartment_Succeeds()
    {
        Department engineering = Department.Create("ENG", "Engineering");
        Department sales = Department.Create("SALES", "Sales");

        IOrganizationRepository repository = FakeRepositoryReturning(engineering, sales);
        DepartmentService service = BuildService(repository);

        Department moved = await service.MoveAsync(engineering.Id, sales.Id, TestContext.Current.CancellationToken);

        moved.ParentDepartmentId.ShouldBe(sales.Id);
    }

    [Fact]
    public async Task MovingADepartmentToTheTopLevel_Succeeds()
    {
        Department parent = Department.Create("CO", "Company");
        Department child = Department.Create("ENG", "Engineering", parent.Id);

        IOrganizationRepository repository = FakeRepositoryReturning(parent, child);
        DepartmentService service = BuildService(repository);

        Department moved = await service.MoveAsync(child.Id, null, TestContext.Current.CancellationToken);

        moved.ParentDepartmentId.ShouldBeNull();
    }

    private static IOrganizationRepository FakeRepositoryReturning(params Department[] departments)
    {
        IOrganizationRepository repository = Substitute.For<IOrganizationRepository>();

        foreach (Department department in departments)
        {
            repository.GetDepartmentAsync(department.Id, Arg.Any<CancellationToken>()).Returns(department);
        }

        return repository;
    }

    private static DepartmentService BuildService(IOrganizationRepository repository)
        => new(repository, Substitute.For<IUnitOfWork>(), NullLogger<DepartmentService>.Instance);
}
