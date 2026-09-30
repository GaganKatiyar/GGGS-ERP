using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Learning;

namespace Gggs.Talent.Application.Learning;

public sealed class EnrollmentService(IEnrollmentRepository enrollments, ICourseRepository courses, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Enrollment> CreateAsync(
        Guid courseId, Guid employeeId, Guid assignedByUserId, DateOnly? dueDate, CancellationToken cancellationToken = default)
    {
        Course course = await courses.GetAsync(courseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Course), courseId);

        Guard(course.IsActive, "COURSE_NOT_ACTIVE", $"Course '{course.Title}' is not active and cannot accept new enrollments.");

        Enrollment enrollment = Enrollment.Create(courseId, employeeId, assignedByUserId, dueDate);
        enrollments.Add(enrollment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return enrollment;
    }

    public async Task<Enrollment> StartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Enrollment enrollment = await GetOrThrowAsync(id, cancellationToken);
        enrollment.Start();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return enrollment;
    }

    public async Task<Enrollment> CompleteAsync(Guid id, decimal? score, CancellationToken cancellationToken = default)
    {
        Enrollment enrollment = await GetOrThrowAsync(id, cancellationToken);
        enrollment.Complete(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), score);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return enrollment;
    }

    public async Task<Enrollment> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Enrollment enrollment = await GetOrThrowAsync(id, cancellationToken);
        enrollment.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return enrollment;
    }

    private async Task<Enrollment> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await enrollments.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Enrollment), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
