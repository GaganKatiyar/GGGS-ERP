using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>Notice and exit on behalf of Separation, through the same service the employee screen uses.</summary>
internal sealed class EmployeeSeparations(AppDbContext dbContext, EmployeeService employees) : IEmployeeSeparations
{
    public Task StartNoticeAsync(Guid employeeId, DateOnly noticeStartDate, CancellationToken cancellationToken = default)
        => employees.StartNoticeAsync(employeeId, new StartNoticeRequest(noticeStartDate), cancellationToken);

    public Task WithdrawNoticeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => employees.WithdrawNoticeAsync(employeeId, cancellationToken);

    public Task ExitAsync(Guid employeeId, DateOnly exitDate, string exitReasonCode, CancellationToken cancellationToken = default)
        => employees.ExitAsync(employeeId, new ExitRequest(exitDate, exitReasonCode), cancellationToken);

    public async Task<IReadOnlyList<Guid>> DirectReportsAsync(Guid managerEmployeeId, CancellationToken cancellationToken = default)
        => await dbContext.Employees.AsNoTracking()
            .Where(employee => employee.ManagerEmployeeId == managerEmployeeId && employee.Status != EmployeeStatus.Exited)
            .Select(employee => employee.Id)
            .ToListAsync(cancellationToken);

    public async Task ChangeManagerAsync(Guid employeeId, Guid newManagerEmployeeId, DateOnly effectiveFrom, string reason, CancellationToken cancellationToken = default)
    {
        Domain.Employee current = await dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Employee), employeeId);
        await employees.ChangeAssignmentAsync(
            employeeId,
            new ChangeAssignmentRequest(
                AssignmentChangeType.ManagerChange,
                effectiveFrom,
                new AssignmentInput(
                    current.DepartmentId, current.PositionId, current.JobId, current.GradeId, current.LocationId, current.LegalEntityId, current.BusinessUnitId,
                    current.DivisionId, newManagerEmployeeId, current.HrbpEmployeeId, current.EmploymentTypeCode, current.WorkModeCode, current.Designation),
                reason),
            cancellationToken);
    }
}
