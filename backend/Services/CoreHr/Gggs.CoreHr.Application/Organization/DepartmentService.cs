using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging;

namespace Gggs.CoreHr.Application.Organization;

/// <summary>
/// Creating, renaming and reparenting departments.
/// <para>
/// Reparenting is the one operation here that needs more than the aggregate itself can see: moving
/// a department under its own descendant would create a cycle the tree cannot be walked out of, and
/// the only way to know that is to walk from the proposed new parent back up to the root and check
/// whether the department being moved appears anywhere in that walk.
/// </para>
/// </summary>
public sealed class DepartmentService(
    IOrganizationRepository organization,
    IUnitOfWork unitOfWork,
    ILogger<DepartmentService> logger)
{
    /// <summary>
    /// Bounds the ancestor walk. A real department tree is a few dozen levels deep at most; hitting
    /// this is a sign of a cycle that already exists, not a legitimately deep organization, and
    /// refusing loudly beats an infinite loop discovering it.
    /// </summary>
    private const int MaxDepth = 200;

    public async Task<Department> CreateAsync(
        string code,
        string name,
        Guid? parentDepartmentId,
        CancellationToken cancellationToken = default)
    {
        string normalizedCode = OrganizationCode.Normalize(code);

        if (await organization.DepartmentCodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new DomainRuleException("ORGANIZATION_CODE_IN_USE", $"A department with code '{normalizedCode}' already exists.");
        }

        if (parentDepartmentId is Guid parentId)
        {
            _ = await organization.GetDepartmentAsync(parentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), parentId);
        }

        Department entity = Department.Create(code, name, parentDepartmentId);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Department {DepartmentId} ({Code}) created.", entity.Id, entity.Code);
        return entity;
    }

    public async Task<Department> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        Department entity = await organization.GetDepartmentAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        entity.Rename(name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Department> SetAssignmentsAsync(
        Guid id,
        Guid? headEmployeeId,
        Guid? costCenterId,
        Guid? businessUnitId,
        CancellationToken cancellationToken = default)
    {
        Department entity = await organization.GetDepartmentAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        if (costCenterId is Guid costCenterIdValue)
        {
            _ = await organization.GetCostCenterAsync(costCenterIdValue, cancellationToken)
                ?? throw new NotFoundException(nameof(CostCenter), costCenterIdValue);
        }

        entity.SetHead(headEmployeeId);
        entity.SetCostCenter(costCenterId);
        entity.SetBusinessUnit(businessUnitId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>Moves a department under a new parent, or to the top level when <paramref name="newParentId"/> is null.</summary>
    public async Task<Department> MoveAsync(Guid id, Guid? newParentId, CancellationToken cancellationToken = default)
    {
        Department entity = await organization.GetDepartmentAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        if (newParentId is Guid parentId)
        {
            Department parent = await organization.GetDepartmentAsync(parentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), parentId);

            await EnsureNoCycleAsync(movingId: id, proposedParent: parent, cancellationToken);
        }

        entity.MoveUnder(newParentId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Department {DepartmentId} moved under {NewParentId}.", id, newParentId);
        return entity;
    }

    /// <summary>
    /// Walks from the proposed parent up to the root. If the department being moved appears in that
    /// walk, the move would make it its own descendant's descendant - a cycle - and is refused.
    /// </summary>
    private async Task EnsureNoCycleAsync(Guid movingId, Department proposedParent, CancellationToken cancellationToken)
    {
        Department? current = proposedParent;

        for (int depth = 0; current is not null; depth++)
        {
            Guard.Against(
                current.Id == movingId,
                "DEPARTMENT_MOVE_WOULD_CREATE_CYCLE",
                "That would make the department a descendant of itself.");

            Guard.Against(
                depth >= MaxDepth,
                "DEPARTMENT_TREE_TOO_DEEP",
                "The department tree is deeper than this operation will walk; it may already contain a cycle.");

            current = current.ParentDepartmentId is Guid parentId
                ? await organization.GetDepartmentAsync(parentId, cancellationToken)
                : null;
        }
    }
}
