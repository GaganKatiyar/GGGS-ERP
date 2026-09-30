using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Application.Snapshots;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

public sealed class JobRequisitionRepository(TalentDbContext dbContext) : IJobRequisitionRepository
{
    public async Task<JobRequisition?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.JobRequisitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(JobRequisition requisition) => dbContext.JobRequisitions.Add(requisition);
}

public sealed class JobPostingRepository(TalentDbContext dbContext) : IJobPostingRepository
{
    public async Task<JobPosting?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.JobPostings.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => await dbContext.JobPostings.AnyAsync(x => x.Slug == slug, cancellationToken);

    public void Add(JobPosting posting) => dbContext.JobPostings.Add(posting);
}

public sealed class CandidateRepository(TalentDbContext dbContext) : ICandidateRepository
{
    public async Task<Candidate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Candidates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Candidate?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => await dbContext.Candidates.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

    public void Add(Candidate candidate) => dbContext.Candidates.Add(candidate);
}

public sealed class JobApplicationRepository(TalentDbContext dbContext) : IJobApplicationRepository
{
    public async Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.JobApplications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> ExistsForCandidateAndPostingAsync(Guid candidateId, Guid postingId, CancellationToken cancellationToken = default)
        => await dbContext.JobApplications.AnyAsync(x => x.CandidateId == candidateId && x.PostingId == postingId, cancellationToken);

    public void Add(JobApplication application) => dbContext.JobApplications.Add(application);
}

public sealed class EmployeeSnapshotRepository(TalentDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
