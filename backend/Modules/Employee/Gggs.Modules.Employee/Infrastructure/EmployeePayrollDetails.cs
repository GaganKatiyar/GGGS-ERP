using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>Statutory and bank details for payroll, masked unless revealed (see <see cref="IEmployeePayrollDetails"/>).</summary>
internal sealed class EmployeePayrollDetails(AppDbContext dbContext, ISensitiveDataProtector protector) : IEmployeePayrollDetails
{
    public async Task<IReadOnlyDictionary<Guid, PayrollIdentity>> GetAsync(IEnumerable<Guid> employeeIds, bool reveal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PayrollIdentity>();
        }

        var rows = await dbContext.Employees.AsNoTracking()
            .Where(employee => ids.Contains(employee.Id))
            .Select(employee => new
            {
                employee.Id,
                employee.Pan,
                employee.Uan,
                employee.EsicNumber,
                employee.ProfessionalTaxStateCode,
                employee.BankName,
                employee.BankIfsc,
                employee.BankAccountHolder,
                employee.BankAccountNumber,
                employee.DateOfBirth,
                employee.GenderCode,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => new PayrollIdentity(
                row.Id,
                Show(SensitiveDataPurposes.Pan, row.Pan, reveal),
                Show(SensitiveDataPurposes.Uan, row.Uan, reveal),
                Show(SensitiveDataPurposes.Esic, row.EsicNumber, reveal),
                row.ProfessionalTaxStateCode,
                row.BankName,
                row.BankIfsc,
                row.BankAccountHolder,
                Show(SensitiveDataPurposes.BankAccount, row.BankAccountNumber, reveal),
                row.DateOfBirth,
                row.GenderCode,
                reveal));
    }

    public async Task<IReadOnlyList<DependentInfo>> DependentsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];
        return ids.Count == 0
            ? []
            : await dbContext.EmployeeDependents.AsNoTracking()
                .Where(dependent => ids.Contains(dependent.EmployeeId))
                .OrderBy(dependent => dependent.FullName)
                .Select(dependent => new DependentInfo(dependent.Id, dependent.EmployeeId, dependent.FullName, dependent.RelationshipCode, dependent.DateOfBirth))
                .ToListAsync(cancellationToken);
    }

    private string? Show(string purpose, ProtectedValue? value, bool reveal)
        => value is null ? null : reveal ? protector.Reveal(purpose, value) : value.Hint;
}
