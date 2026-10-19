using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Learning.Domain;

public enum CourseFormat
{
    Online = 1,
    Classroom = 2,
    Virtual = 3,
    Blended = 4,
}

public sealed record CourseDetails(
    string Code,
    string Title,
    string? Description,
    string? Category,
    CourseFormat Format,
    string? Provider,
    decimal DurationHours,
    string? ContentUrl,
    bool Mandatory,
    bool AssignToNewJoiners,
    int? CompletionDays,
    int? CertificateValidityMonths,
    IReadOnlyList<string>? Skills);

/// <summary>
/// A course in the catalogue (spec: learning): online, classroom, virtual or blended; mandatory or
/// optional; given to joiners automatically if set; issuing a certificate that expires when it must be
/// renewed.
/// </summary>
public sealed class Course : TenantAggregateRoot
{
    private Course()
    {
        Code = null!;
        Title = null!;
        SkillsJson = "[]";
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public string? Category { get; private set; }

    public CourseFormat Format { get; private set; }

    public string? Provider { get; private set; }

    public decimal DurationHours { get; private set; }

    public string? ContentUrl { get; private set; }

    public bool Mandatory { get; private set; }

    public bool AssignToNewJoiners { get; private set; }

    /// <summary>Days an assignee has to complete it, from assignment (or joining).</summary>
    public int? CompletionDays { get; private set; }

    /// <summary>When set, completing it earns a certificate valid this many months.</summary>
    public int? CertificateValidityMonths { get; private set; }

    public string SkillsJson { get; private set; }

    public IReadOnlyList<string> Skills => JsonSerializer.Deserialize<List<string>>(SkillsJson) ?? [];

    public static Course Create(CourseDetails details)
    {
        var course = new Course();
        course.Update(details, active: true);
        return course;
    }

    public void Update(CourseDetails details, bool active)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.DurationHours is < 0 or > 1000 || details.CompletionDays is < 1 or > 365 || details.CertificateValidityMonths is < 1 or > 120)
        {
            throw new DomainRuleException("COURSE_DETAILS", "Duration is up to 1,000 hours, completion within a year, certificates valid up to ten years.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Code).Trim().ToUpperInvariant(), 30);
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 200);
        Description = Optional(details.Description, 4000);
        Category = Optional(details.Category, 80);
        Format = details.Format;
        Provider = Optional(details.Provider, 200);
        DurationHours = details.DurationHours;
        ContentUrl = Optional(details.ContentUrl, 500);
        Mandatory = details.Mandatory;
        AssignToNewJoiners = details.AssignToNewJoiners;
        CompletionDays = details.CompletionDays;
        CertificateValidityMonths = details.CertificateValidityMonths;
        SkillsJson = JsonSerializer.Serialize((details.Skills ?? []).Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => Guard.MaxLength(skill.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        IsActive = active;
    }

    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

/// <summary>An ordered set of courses taken together - a role curriculum or a certification track.</summary>
public sealed class LearningPath : TenantAggregateRoot
{
    private LearningPath()
    {
        Code = null!;
        Title = null!;
        CoursesJson = "[]";
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public string CoursesJson { get; private set; }

    public IReadOnlyList<Guid> CourseIds => JsonSerializer.Deserialize<List<Guid>>(CoursesJson) ?? [];

    public static LearningPath Create(string code, string title, string? description, IReadOnlyList<Guid> courseIds)
    {
        var path = new LearningPath();
        path.Update(code, title, description, courseIds, active: true);
        return path;
    }

    public void Update(string code, string title, string? description, IReadOnlyList<Guid> courseIds, bool active)
    {
        List<Guid> courses = [.. (courseIds ?? []).Where(id => id != Guid.Empty).Distinct()];
        if (courses.Count == 0)
        {
            throw new DomainRuleException("PATH_COURSES", "A path has at least one course.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30);
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 2000);
        CoursesJson = JsonSerializer.Serialize(courses);
        IsActive = active;
    }
}

public enum EnrollmentSource
{
    Self = 1,
    Assigned = 2,
    Joiner = 3,
    Renewal = 4,
    Path = 5,
}

public enum EnrollmentStatus
{
    Assigned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,

    /// <summary>Completed, but the certificate it earned has lapsed.</summary>
    Expired = 5,
}

/// <summary>Someone taking a course: assigned or self-enrolled, their progress, completion and certificate.</summary>
public sealed class Enrollment : TenantAggregateRoot
{
    private Enrollment()
    {
    }

    public Guid CourseId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid? PathId { get; private set; }

    public EnrollmentSource Source { get; private set; }

    public Guid? AssignedByEmployeeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public int Progress { get; private set; }

    public DateOnly? StartedOn { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public decimal? Score { get; private set; }

    public string? CertificateNumber { get; private set; }

    public DateOnly? CertificateExpiresOn { get; private set; }

    public int? Rating { get; private set; }

    public string? Feedback { get; private set; }

    public DateOnly? LastRemindedOn { get; private set; }

    public bool IsOpen => Status is EnrollmentStatus.Assigned or EnrollmentStatus.InProgress;

    public static Enrollment Enroll(Guid courseId, Guid employeeId, EnrollmentSource source, Guid? assignedBy, DateOnly? dueDate, Guid? pathId) => new()
    {
        CourseId = Guard.NotEmpty(courseId),
        EmployeeId = Guard.NotEmpty(employeeId),
        Source = source,
        AssignedByEmployeeId = assignedBy,
        DueDate = dueDate,
        PathId = pathId,
        Status = EnrollmentStatus.Assigned,
    };

    public void Start(DateOnly today)
    {
        EnsureOpen();
        Status = EnrollmentStatus.InProgress;
        StartedOn ??= today;
    }

    public void UpdateProgress(int progress, DateOnly today)
    {
        EnsureOpen();
        if (progress is < 0 or > 99)
        {
            throw new DomainRuleException("ENROLLMENT_PROGRESS", "Progress is 0-99%; mark the course complete to finish it.");
        }

        Progress = progress;
        Status = EnrollmentStatus.InProgress;
        StartedOn ??= today;
    }

    /// <summary>Completes the course; a course with a certificate validity issues a numbered certificate that expires.</summary>
    public void Complete(DateOnly completedOn, decimal? score, int? certificateValidityMonths, string? certificateNumber)
    {
        EnsureOpen();
        if (score is < 0 or > 100)
        {
            throw new DomainRuleException("ENROLLMENT_SCORE", "A score is 0-100.");
        }

        Status = EnrollmentStatus.Completed;
        Progress = 100;
        StartedOn ??= completedOn;
        CompletedOn = completedOn;
        Score = score;
        if (certificateValidityMonths is { } months)
        {
            CertificateNumber = Guard.MaxLength(Guard.NotNullOrWhiteSpace(certificateNumber), 40);
            CertificateExpiresOn = completedOn.AddMonths(months);
        }
    }

    public void Rate(int rating, string? feedback)
    {
        if (Status != EnrollmentStatus.Completed && Status != EnrollmentStatus.Expired)
        {
            throw new DomainRuleException("ENROLLMENT_STATE", "Rate a course once you have completed it.");
        }

        if (rating is < 1 or > 5)
        {
            throw new DomainRuleException("ENROLLMENT_RATING", "A rating is 1 to 5 stars.");
        }

        Rating = rating;
        Feedback = string.IsNullOrWhiteSpace(feedback) ? null : Guard.MaxLength(feedback.Trim(), 1000);
    }

    public void Cancel()
    {
        EnsureOpen();
        Status = EnrollmentStatus.Cancelled;
    }

    public void Expire()
    {
        if (Status != EnrollmentStatus.Completed || CertificateExpiresOn is null)
        {
            throw new DomainRuleException("ENROLLMENT_STATE", "Only a completed course with a certificate expires.");
        }

        Status = EnrollmentStatus.Expired;
    }

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is { } due && due < today;

    /// <summary>An overdue reminder once a week while overdue.</summary>
    public bool NeedsOverdueReminder(DateOnly today) => IsOverdue(today) && (LastRemindedOn is null || LastRemindedOn.Value.AddDays(7) <= today);

    /// <summary>One reminder when the certificate is 30 days from expiry.</summary>
    public bool NeedsExpiryReminder(DateOnly today) =>
        Status == EnrollmentStatus.Completed && CertificateExpiresOn is { } expires && expires > today && expires <= today.AddDays(30)
        && (LastRemindedOn is null || LastRemindedOn < expires.AddDays(-30));

    public void Reminded(DateOnly today) => LastRemindedOn = today;

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("ENROLLMENT_STATE", $"The enrollment is {Status.ToString().ToLowerInvariant()}.");
        }
    }
}

/// <summary>The courses every tenant starts with: the usual compliance training for everyone who joins.</summary>
public static class LearningDefaults
{
    public static IReadOnlyList<CourseDetails> Courses { get; } =
    [
        new("COC", "Code of conduct", "How we work: integrity, conflicts of interest, gifts, and speaking up.", "Compliance", CourseFormat.Online, null, 1, null, true, true, 30, 12, null),
        new("INFOSEC", "Information security awareness", "Passwords, phishing, data handling and reporting an incident.", "Compliance", CourseFormat.Online, null, 1.5m, null, true, true, 30, 12, ["Information security"]),
        new("POSH", "Prevention of sexual harassment (POSH)", "The POSH Act 2013, the internal committee, and how to raise a complaint.", "Compliance", CourseFormat.Online, null, 1, null, true, true, 30, 12, null),
        new("DATAPRIV", "Data privacy and DPDP", "Personal data under the Digital Personal Data Protection Act.", "Compliance", CourseFormat.Online, null, 1, null, false, false, null, 24, ["Data privacy"]),
    ];
}
