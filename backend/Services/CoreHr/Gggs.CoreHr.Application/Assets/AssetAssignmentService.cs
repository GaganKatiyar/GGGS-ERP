using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Domain.Assets;
using Gggs.CoreHr.Domain.Employees;

namespace Gggs.CoreHr.Application.Assets;

/// <summary>
/// Assigning an <see cref="Asset"/> to an employee and recording its return - always changes the
/// asset's own <see cref="Asset.Status"/> in the same operation, so the two never drift (see
/// <see cref="Asset"/>'s own remarks).
/// </summary>
public sealed class AssetAssignmentService(
    IAssetAssignmentRepository assignments, IAssetRepository assets, IEmployeeRepository employees,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<AssetAssignment> AssignAsync(Guid assetId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Asset asset = await assets.GetAsync(assetId, cancellationToken)
            ?? throw new NotFoundException(nameof(Asset), assetId);

        Employee? employee = await employees.GetAsync(employeeId, cancellationToken);
        Guard(employee is not null, "ASSET_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != EmployeeStatus.Exited, "ASSET_EMPLOYEE_EXITED", "This employee has already exited.");

        asset.MarkAssigned();
        AssetAssignment assignment = AssetAssignment.Create(assetId, employeeId, clock.GetUtcNow().UtcDateTime);
        assignments.Add(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<AssetAssignment> ReturnAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        AssetAssignment assignment = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(AssetAssignment), assignmentId);

        Asset asset = await assets.GetAsync(assignment.AssetId, cancellationToken)
            ?? throw new NotFoundException(nameof(Asset), assignment.AssetId);

        assignment.Return(clock.GetUtcNow().UtcDateTime);
        asset.MarkReturned();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
