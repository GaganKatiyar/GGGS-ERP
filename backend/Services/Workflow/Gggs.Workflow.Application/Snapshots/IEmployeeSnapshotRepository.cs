using Gggs.Workflow.Domain.Snapshots;

namespace Gggs.Workflow.Application.Snapshots;

public interface IEmployeeSnapshotRepository
{
    Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeSnapshot snapshot);
}
