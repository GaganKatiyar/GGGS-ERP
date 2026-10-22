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

    public async Task<IReadOnlyList<UpcomingExit>> UpcomingExitsAsync(int retirementAge, DateOnly until, CancellationToken cancellationToken = default)
    {
        var people = await dbContext.Employees.AsNoTracking()
            .Where(employee => employee.Status != EmployeeStatus.Exited && (employee.DateOfBirth != null || employee.ContractEndDate != null))
            .Select(employee => new { employee.Id, employee.DateOfBirth, employee.ContractEndDate })
            .ToListAsync(cancellationToken);
        List<UpcomingExit> due = [];
        foreach (var person in people)
        {
            if (person.DateOfBirth is { } born && RetirementDate(born, retirementAge) is var retires && retires <= until
                && (person.ContractEndDate is null || retires <= person.ContractEndDate))
            {
                due.Add(new UpcomingExit(person.Id, UpcomingExitKind.Retirement, retires));
            }
            else if (person.ContractEndDate is { } ends && ends <= until)
            {
                due.Add(new UpcomingExit(person.Id, UpcomingExitKind.ContractEnd, ends));
            }
        }

        return due;
    }

    /// <summary>Superannuation takes effect on the last day of the month in which the age is reached.</summary>
    internal static DateOnly RetirementDate(DateOnly dateOfBirth, int retirementAge)
    {
        DateOnly birthday = dateOfBirth.AddYears(retirementAge);
        return new DateOnly(birthday.Year, birthday.Month, DateTime.DaysInMonth(birthday.Year, birthday.Month));
    }

    public async Task<IReadOnlyDictionary<Guid, PersonalContact>> PersonalContactsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. employeeIds.Distinct()];
        return await dbContext.Employees.AsNoTracking()
            .Where(employee => ids.Contains(employee.Id))
            .ToDictionaryAsync(employee => employee.Id, employee => new PersonalContact(employee.PersonalEmail, employee.MobilePhone), cancellationToken);
    }

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
