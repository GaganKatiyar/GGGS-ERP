using Gggs.Time.Domain.Snapshots;

namespace Gggs.Time.Application.Snapshots;

public interface IEmployeeSnapshotRepository
{
    Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSnapshot snapshot);
}
