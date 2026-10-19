using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>Hires on behalf of other modules through the same service the hire screen uses.</summary>
internal sealed class EmployeeHiring(EmployeeService employees) : IEmployeeHiring
{
    public async Task<(Guid EmployeeId, string EmployeeCode)> HireAsync(NewHire hire, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hire);
        var employee = await employees.HireAsync(
            new HireEmployeeRequest(
                null, hire.FirstName, hire.MiddleName, hire.LastName, null, null, null, hire.PersonalEmail, null, hire.MobilePhone, hire.JoiningDate, null,
                new AssignmentInput(
                    hire.DepartmentId, hire.PositionId, hire.JobId, hire.GradeId, hire.LocationId, null, null, null, hire.ManagerEmployeeId, null,
                    hire.EmploymentTypeCode, hire.WorkModeCode, hire.Designation)),
            cancellationToken);
        return (employee.Id, employee.EmployeeCode);
    }
}
