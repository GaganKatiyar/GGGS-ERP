using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

public interface IJobPostingRepository
{
    Task<JobPosting?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    void Add(JobPosting posting);
}
