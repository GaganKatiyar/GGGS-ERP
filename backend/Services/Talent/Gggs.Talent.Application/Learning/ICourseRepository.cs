using Gggs.Talent.Domain.Learning;

namespace Gggs.Talent.Application.Learning;

public interface ICourseRepository
{
    Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(Course course);
}

public interface IEnrollmentRepository
{
    Task<Enrollment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Enrollment enrollment);
}
