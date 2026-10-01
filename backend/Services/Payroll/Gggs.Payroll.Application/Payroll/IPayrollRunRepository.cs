using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public interface IPayGroupRepository
{
    Task<PayGroup?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(PayGroup payGroup);
}

public interface IPayPeriodRepository
{
    Task<PayPeriod?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> PeriodCodeExistsAsync(Guid payGroupId, string periodCode, CancellationToken cancellationToken = default);

    void Add(PayPeriod period);
}

public interface IPayrollRunRepository
{
    Task<PayrollRun?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> RunNoExistsAsync(string runNo, CancellationToken cancellationToken = default);

    void Add(PayrollRun run);
}

public interface IPayrollRunEmployeeRepository
{
    Task<IReadOnlyList<PayrollRunEmployee>> ListForRunAsync(Guid payrollRunId, CancellationToken cancellationToken = default);

    void Add(PayrollRunEmployee runEmployee);

    /// <summary>Deletes every prior result row for this run - what a recalculation clears before
    /// writing fresh ones, since a run's employee results are replaced wholesale, never adjusted in
    /// place (see <see cref="PayrollRun"/>'s own remarks).</summary>
    void RemoveAllForRun(IEnumerable<PayrollRunEmployee> runEmployees);
}

public interface IPayrollRunLineRepository
{
    Task<IReadOnlyList<PayrollRunLine>> ListForRunEmployeesAsync(
        IEnumerable<Guid> payrollRunEmployeeIds, CancellationToken cancellationToken = default);

    void Add(PayrollRunLine line);

    void RemoveAllForRunEmployees(IEnumerable<PayrollRunLine> lines);
}
