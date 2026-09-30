using Gggs.Talent.Domain.Snapshots;

namespace Gggs.Talent.Application.Snapshots;

public interface IEmployeeSnapshotRepository
{
    Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSnapshot snapshot);
}
