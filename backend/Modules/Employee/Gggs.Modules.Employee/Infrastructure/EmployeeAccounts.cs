using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

internal sealed class EmployeeAccounts(AppDbContext dbContext) : IEmployeeAccounts
{
    public async Task<AccountHolder?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.Employees.AsNoTracking()
            .Where(employee => employee.Id == employeeId)
            .Select(employee => new AccountHolder(employee.Id, employee.EmployeeCode, employee.FirstName, employee.LastName, employee.DisplayName, employee.OfficialEmail, employee.PersonalEmail))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> SetOfficialEmailIfEmptyAsync(Guid employeeId, string email, CancellationToken cancellationToken = default)
    {
        Domain.Employee? employee = await dbContext.Employees.FirstOrDefaultAsync(row => row.Id == employeeId, cancellationToken);
        if (employee is null || !string.IsNullOrWhiteSpace(employee.OfficialEmail))
        {
            return false;
        }

        string normalized = email.Trim().ToLowerInvariant();
        if (await dbContext.Employees.AsNoTracking().AnyAsync(row => row.OfficialEmail == normalized && row.Id != employeeId, cancellationToken))
        {
            return false;
        }

        employee.UpdateContact(employee.PersonalEmail, normalized, employee.MobilePhone, employee.AlternatePhone, employee.CurrentAddress, employee.PermanentAddress);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
