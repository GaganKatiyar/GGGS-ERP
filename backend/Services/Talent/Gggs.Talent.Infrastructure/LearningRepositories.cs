using Gggs.Talent.Application.Learning;
using Gggs.Talent.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Infrastructure;

public sealed class CourseRepository(TalentDbContext dbContext) : ICourseRepository
{
    public async Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Courses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.Courses.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(Course course) => dbContext.Courses.Add(course);
}

public sealed class EnrollmentRepository(TalentDbContext dbContext) : IEnrollmentRepository
{
    public async Task<Enrollment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Enrollments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Enrollment enrollment) => dbContext.Enrollments.Add(enrollment);
}
