using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>Notice and exit on behalf of Separation, through the same service the employee screen uses.</summary>
internal sealed class EmployeeSeparations(EmployeeService employees) : IEmployeeSeparations
{
    public Task StartNoticeAsync(Guid employeeId, DateOnly noticeStartDate, CancellationToken cancellationToken = default)
        => employees.StartNoticeAsync(employeeId, new StartNoticeRequest(noticeStartDate), cancellationToken);

    public Task WithdrawNoticeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => employees.WithdrawNoticeAsync(employeeId, cancellationToken);

    public Task ExitAsync(Guid employeeId, DateOnly exitDate, string exitReasonCode, CancellationToken cancellationToken = default)
        => employees.ExitAsync(employeeId, new ExitRequest(exitDate, exitReasonCode), cancellationToken);
}
