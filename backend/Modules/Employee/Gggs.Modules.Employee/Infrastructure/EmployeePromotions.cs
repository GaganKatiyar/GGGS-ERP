using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>Promotes on behalf of other modules through the same assignment change the employee screen uses.</summary>
internal sealed class EmployeePromotions(AppDbContext dbContext, EmployeeService employees) : IEmployeePromotions
{
    public async Task PromoteAsync(Promotion promotion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        Domain.Employee current = await dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(employee => employee.Id == promotion.EmployeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Employee), promotion.EmployeeId);
        await employees.ChangeAssignmentAsync(
            promotion.EmployeeId,
            new ChangeAssignmentRequest(
                AssignmentChangeType.Promotion,
                promotion.EffectiveFrom,
                new AssignmentInput(
                    current.DepartmentId, promotion.PositionId ?? current.PositionId, promotion.JobId ?? current.JobId, promotion.GradeId ?? current.GradeId,
                    current.LocationId, current.LegalEntityId, current.BusinessUnitId, current.DivisionId, current.ManagerEmployeeId, current.HrbpEmployeeId,
                    current.EmploymentTypeCode, current.WorkModeCode, string.IsNullOrWhiteSpace(promotion.Designation) ? current.Designation : promotion.Designation),
                promotion.Reason),
            cancellationToken);
    }
}
