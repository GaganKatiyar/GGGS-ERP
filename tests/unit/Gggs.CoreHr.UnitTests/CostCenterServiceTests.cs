using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>The cost-center mirror of <see cref="DepartmentServiceTests"/>, for its own independent tree.</summary>
public sealed class CostCenterServiceTests
{
    [Fact]
    public async Task MovingACostCenterUnderItself_IsRefused()
    {
        CostCenter costCenter = CostCenter.Create("CC-ENG", "Engineering");
        IOrganizationRepository repository = FakeRepositoryReturning(costCenter);

        CostCenterService service = BuildService(repository);

        // As with departments, the service's ancestor walk catches "parent is myself" as a
        // one-node cycle before CostCenter.MoveUnder's own guard would ever run.
        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.MoveAsync(costCenter.Id, costCenter.Id));

        exception.ErrorCode.ShouldBe("COST_CENTER_MOVE_WOULD_CREATE_CYCLE");
    }

    [Fact]
    public async Task MovingACostCenterUnderItsOwnGrandchild_IsRefused()
    {
        // Company -> Engineering -> Platform. Moving Company under Platform would make Company a
        // descendant of its own descendant.
        CostCenter company = CostCenter.Create("CC-CO", "Company");
        CostCenter engineering = CostCenter.Create("CC-ENG", "Engineering");
        engineering.MoveUnder(company.Id);
        CostCenter platform = CostCenter.Create("CC-PLATFORM", "Platform");
        platform.MoveUnder(engineering.Id);

        IOrganizationRepository repository = FakeRepositoryReturning(company, engineering, platform);
        CostCenterService service = BuildService(repository);

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.MoveAsync(company.Id, platform.Id));

        exception.ErrorCode.ShouldBe("COST_CENTER_MOVE_WOULD_CREATE_CYCLE");
    }

    [Fact]
    public async Task MovingACostCenterUnderAnUnrelatedCostCenter_Succeeds()
    {
        CostCenter engineering = CostCenter.Create("CC-ENG", "Engineering");
        CostCenter sales = CostCenter.Create("CC-SALES", "Sales");

        IOrganizationRepository repository = FakeRepositoryReturning(engineering, sales);
        CostCenterService service = BuildService(repository);

        CostCenter moved = await service.MoveAsync(engineering.Id, sales.Id, TestContext.Current.CancellationToken);

        moved.ParentCostCenterId.ShouldBe(sales.Id);
    }

    private static IOrganizationRepository FakeRepositoryReturning(params CostCenter[] costCenters)
    {
        IOrganizationRepository repository = Substitute.For<IOrganizationRepository>();

        foreach (CostCenter costCenter in costCenters)
        {
            repository.GetCostCenterAsync(costCenter.Id, Arg.Any<CancellationToken>()).Returns(costCenter);
        }

        return repository;
    }

    private static CostCenterService BuildService(IOrganizationRepository repository)
        => new(repository, Substitute.For<IUnitOfWork>(), NullLogger<CostCenterService>.Instance);
}
