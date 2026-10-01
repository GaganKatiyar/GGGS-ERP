using Gggs.Spend.Domain.Snapshots;

namespace Gggs.Spend.Application.Snapshots;

public interface IEmployeeSnapshotRepository
{
    Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSnapshot snapshot);
}
