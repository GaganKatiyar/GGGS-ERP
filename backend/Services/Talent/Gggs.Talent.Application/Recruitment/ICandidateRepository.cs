using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

public interface ICandidateRepository
{
    Task<Candidate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Candidate?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(Candidate candidate);
}

public interface IJobApplicationRepository
{
    Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForCandidateAndPostingAsync(Guid candidateId, Guid postingId, CancellationToken cancellationToken = default);

    void Add(JobApplication application);
}
