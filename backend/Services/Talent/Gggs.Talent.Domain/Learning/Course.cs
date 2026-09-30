using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Learning;

/// <summary>
/// A catalog entry an employee can be enrolled against - "Workplace Safety," "Advanced Excel." No
/// session scheduling, classroom/online type, external content link or certification link in this
/// v1 slice (architecture.md's <c>TrainingSession</c>/<c>Certification</c>): a course is a named,
/// durable thing employees complete and record a score against, matching every other v1 module's
/// established simplification (Performance v1's flat cycle instead of configurable phases).
/// </summary>
public sealed class Course : TenantAggregateRoot
{
    private Course()
    {
        Code = null!;
        Title = null!;
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public decimal DurationHours { get; private set; }

    public static Course Create(string code, string title, string? description, decimal durationHours)
    {
        Guard.Against(durationHours <= 0, "COURSE_DURATION_MUST_BE_POSITIVE", "The duration must be greater than zero hours.");

        return new Course
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 1000),
            DurationHours = durationHours,
            IsActive = true,
        };
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
