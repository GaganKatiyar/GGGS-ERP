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

    /// <summary>Every employee's current salary - one row per employee, the same "latest by
    /// EffectiveFrom/CreatedDate" resolution <see cref="GetCurrentForEmployeeAsync"/> uses for one.
    /// What a <see cref="PayrollRun"/> calculation iterates: everyone with a salary assigned is in
    /// the run, since this v1 slice has no pay-group employee roster of its own yet.</summary>
    Task<IReadOnlyList<EmployeeSalary>> ListAllCurrentAsync(CancellationToken cancellationToken = default);

    void Add(EmployeeSalary salary);
}

public interface IEmployeeSalaryComponentRepository
{
    Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalaryAsync(Guid employeeSalaryId, CancellationToken cancellationToken = default);

    /// <summary>The same lookup as <see cref="ListForSalaryAsync"/>, batched across every employee
    /// a <see cref="PayrollRun"/> calculation is processing, so it is one query instead of one per
    /// employee.</summary>
    Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalariesAsync(
        IEnumerable<Guid> employeeSalaryIds, CancellationToken cancellationToken = default);

    void Add(EmployeeSalaryComponent component);
}
