using Gggs.CoreHr.Domain.Employees;

namespace Gggs.CoreHr.Application.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> EmployeeCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Employee employee);

    Task<EmploymentRecord?> GetCurrentEmploymentRecordAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmploymentRecord>> GetEmploymentHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmploymentRecord record);
}
