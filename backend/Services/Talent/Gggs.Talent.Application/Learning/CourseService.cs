using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Learning;

namespace Gggs.Talent.Application.Learning;

public sealed class CourseService(ICourseRepository courses, IUnitOfWork unitOfWork)
{
    public async Task<Course> CreateAsync(
        string code, string title, string? description, decimal durationHours, CancellationToken cancellationToken = default)
    {
        Guard(
            !await courses.CodeExistsAsync(code, cancellationToken),
            "COURSE_CODE_ALREADY_EXISTS",
            $"A course with code '{code}' already exists.");

        Course course = Course.Create(code, title, description, durationHours);
        courses.Add(course);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return course;
    }

    public async Task<Course> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Course course = await GetOrThrowAsync(id, cancellationToken);
        course.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return course;
    }

    public async Task<Course> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Course course = await GetOrThrowAsync(id, cancellationToken);
        course.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return course;
    }

    private async Task<Course> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await courses.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Course), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
