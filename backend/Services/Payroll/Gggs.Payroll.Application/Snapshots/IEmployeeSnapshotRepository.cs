using Gggs.Payroll.Domain.Snapshots;

namespace Gggs.Payroll.Application.Snapshots;

public interface IEmployeeSnapshotRepository
{
    Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSnapshot snapshot);
}
