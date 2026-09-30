using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

public interface IJobRequisitionRepository
{
    Task<JobRequisition?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(JobRequisition requisition);
}
