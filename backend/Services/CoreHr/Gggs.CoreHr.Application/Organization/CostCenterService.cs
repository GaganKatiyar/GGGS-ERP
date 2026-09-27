using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging;

namespace Gggs.CoreHr.Application.Organization;

/// <summary>Creating, renaming and reparenting cost centers - the mirror of <see cref="DepartmentService"/> for its own, independent tree.</summary>
public sealed class CostCenterService(
    IOrganizationRepository organization,
    IUnitOfWork unitOfWork,
    ILogger<CostCenterService> logger)
{
    private const int MaxDepth = 200;

    public async Task<CostCenter> CreateAsync(
        string code,
        string name,
        string? glAccountCode,
        CancellationToken cancellationToken = default)
    {
        string normalizedCode = OrganizationCode.Normalize(code);

        if (await organization.CostCenterCodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new DomainRuleException("ORGANIZATION_CODE_IN_USE", $"A cost center with code '{normalizedCode}' already exists.");
        }

        CostCenter entity = CostCenter.Create(code, name, glAccountCode);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Cost center {CostCenterId} ({Code}) created.", entity.Id, entity.Code);
        return entity;
    }

    public async Task<CostCenter> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        CostCenter entity = await organization.GetCostCenterAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(CostCenter), id);

        entity.Rename(name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<CostCenter> SetOwnerAsync(Guid id, Guid? ownerEmployeeId, CancellationToken cancellationToken = default)
    {
        CostCenter entity = await organization.GetCostCenterAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(CostCenter), id);

        entity.SetOwner(ownerEmployeeId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<CostCenter> MoveAsync(Guid id, Guid? newParentId, CancellationToken cancellationToken = default)
    {
        CostCenter entity = await organization.GetCostCenterAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(CostCenter), id);

        if (newParentId is Guid parentId)
        {
            CostCenter parent = await organization.GetCostCenterAsync(parentId, cancellationToken)
                ?? throw new NotFoundException(nameof(CostCenter), parentId);

            await EnsureNoCycleAsync(movingId: id, proposedParent: parent, cancellationToken);
        }

        entity.MoveUnder(newParentId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task EnsureNoCycleAsync(Guid movingId, CostCenter proposedParent, CancellationToken cancellationToken)
    {
        CostCenter? current = proposedParent;

        for (int depth = 0; current is not null; depth++)
        {
            Guard.Against(
                current.Id == movingId,
                "COST_CENTER_MOVE_WOULD_CREATE_CYCLE",
                "That would make the cost center a descendant of itself.");

            Guard.Against(
                depth >= MaxDepth,
                "COST_CENTER_TREE_TOO_DEEP",
                "The cost center tree is deeper than this operation will walk; it may already contain a cycle.");

            current = current.ParentCostCenterId is Guid parentId
                ? await organization.GetCostCenterAsync(parentId, cancellationToken)
                : null;
        }
    }
}
