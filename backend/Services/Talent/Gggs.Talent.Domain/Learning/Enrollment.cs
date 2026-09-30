using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Learning;

public enum EnrollmentStatus
{
    Assigned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
}

/// <summary>
/// One employee's assignment to a <see cref="Course"/> - Assigned -&gt; InProgress -&gt; Completed,
/// or Cancelled from either open state. No training-session link in this v1 slice: an enrollment is
/// against the course itself, not a scheduled run of it.
/// </summary>
public sealed class Enrollment : TenantAggregateRoot
{
    private Enrollment()
    {
    }

    public Guid CourseId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid AssignedByUserId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public DateOnly? CompletedDate { get; private set; }

    public decimal? Score { get; private set; }

    public static Enrollment Create(Guid courseId, Guid employeeId, Guid assignedByUserId, DateOnly? dueDate) => new()
    {
        CourseId = Guard.NotEmpty(courseId),
        EmployeeId = Guard.NotEmpty(employeeId),
        AssignedByUserId = Guard.NotEmpty(assignedByUserId),
        DueDate = dueDate,
        Status = EnrollmentStatus.Assigned,
        IsActive = true,
    };

    public void Start()
    {
        Guard.Against(
            Status != EnrollmentStatus.Assigned, "ENROLLMENT_NOT_ASSIGNED", $"Enrollment must be Assigned to start; it is {Status}.");

        Status = EnrollmentStatus.InProgress;
    }

    public void Complete(DateOnly completedDate, decimal? score)
    {
        Guard.Against(
            Status is not (EnrollmentStatus.Assigned or EnrollmentStatus.InProgress),
            "ENROLLMENT_NOT_OPEN",
            $"Enrollment must be Assigned or InProgress to complete; it is {Status}.");
        Guard.Against(score is < 0 or > 100, "ENROLLMENT_SCORE_OUT_OF_RANGE", "The score must be between 0 and 100.");

        Status = EnrollmentStatus.Completed;
        CompletedDate = completedDate;
        Score = score;
    }

    public void Cancel()
    {
        Guard.Against(
            Status is not (EnrollmentStatus.Assigned or EnrollmentStatus.InProgress),
            "ENROLLMENT_NOT_OPEN",
            $"Enrollment must be Assigned or InProgress to cancel; it is {Status}.");

        Status = EnrollmentStatus.Cancelled;
    }
}
