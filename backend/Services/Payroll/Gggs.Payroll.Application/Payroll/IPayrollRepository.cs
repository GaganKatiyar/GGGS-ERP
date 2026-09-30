using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public interface ISalaryComponentRepository
{
    Task<SalaryComponent?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalaryComponent>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(SalaryComponent component);
}

public interface ISalaryStructureRepository
{
    Task<SalaryStructure?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(SalaryStructure salaryStructure);
}

public interface ISalaryStructureComponentRepository
{
    Task<IReadOnlyList<SalaryStructureComponent>> ListForStructureAsync(Guid structureId, CancellationToken cancellationToken = default);

    void Add(SalaryStructureComponent structureComponent);
}

public interface IEmployeeSalaryRepository
{
    Task<EmployeeSalary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EmployeeSalary?> GetCurrentForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSalary salary);
}

public interface IEmployeeSalaryComponentRepository
{
    Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalaryAsync(Guid employeeSalaryId, CancellationToken cancellationToken = default);

    void Add(EmployeeSalaryComponent component);
}
