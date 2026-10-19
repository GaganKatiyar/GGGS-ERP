using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Learning.Domain;
using Gggs.Modules.Learning.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Learning.Application;

public sealed record CourseItem(
    Guid Id,
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
    IReadOnlyList<string> Skills,
    bool IsActive,
    int Enrolled,
    int Completed,
    decimal? AverageRating);

public sealed record CourseBody(CourseDetails Details, bool IsActive = true);

public sealed record PathItem(Guid Id, string Code, string Title, string? Description, IReadOnlyList<Guid> CourseIds, IReadOnlyList<string> CourseTitles, decimal DurationHours, bool IsActive);

public sealed record PathBody(string Code, string Title, string? Description, IReadOnlyList<Guid> CourseIds, bool IsActive = true);

public sealed record EnrollmentItem(
    Guid Id,
    Guid CourseId,
    string CourseCode,
    string CourseTitle,
    CourseFormat Format,
    decimal DurationHours,
    string? ContentUrl,
    bool Mandatory,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid? PathId,
    EnrollmentSource Source,
    string? AssignedByName,
    DateOnly? DueDate,
    EnrollmentStatus Status,
    int Progress,
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    decimal? Score,
    string? CertificateNumber,
    DateOnly? CertificateExpiresOn,
    int? Rating,
    bool Overdue,
    bool CanUpdate);

public sealed record EnrollmentQuery
{
    public Guid? CourseId { get; init; }

    public Guid? EmployeeId { get; init; }

    public EnrollmentStatus? Status { get; init; }

    public bool? Overdue { get; init; }
}

public sealed record AssignBody(IReadOnlyList<Guid> EmployeeIds, Guid? CourseId, Guid? PathId, DateOnly? DueDate);

public sealed record AssignResult(int Assigned, int Skipped);

public sealed record ProgressBody(int Progress);

public sealed record CompleteBody(DateOnly? CompletedOn, decimal? Score);

public sealed record RateBody(int Rating, string? Feedback);

public sealed record CatalogEntry(CourseItem Course, EnrollmentItem? Mine);

public sealed record MyLearning(IReadOnlyList<EnrollmentItem> Active, IReadOnlyList<EnrollmentItem> Completed, IReadOnlyList<EnrollmentItem> Certificates, decimal HoursThisYear);

public sealed record LearningDashboard(
    int Assigned,
    int InProgress,
    int CompletedLast90Days,
    int Overdue,
    decimal? MandatoryCompliancePercent,
    int CertificatesExpiringIn30Days,
    int CertificatesExpired,
    IReadOnlyList<CourseItem> TopCourses);

/// <summary>
/// Learning (spec: courses, paths, certifications and expiry): the catalogue, assignments (to joiners
/// automatically for mandatory courses), self-enrollment, progress and completion, certificates that
/// lapse and are renewed, and the compliance picture.
/// </summary>
internal sealed class LearningService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ITenantClock clock)
{
    // ----- Catalogue ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<CourseItem>> CoursesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<Course> rows = dbContext.Courses.AsNoTracking();
        if (!includeInactive)
        {
            rows = rows.Where(row => row.IsActive);
        }

        return await CourseItemsAsync(await rows.OrderBy(row => row.Category).ThenBy(row => row.Title).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<CourseItem>> SaveCourseAsync(Guid? id, CourseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = (body.Details.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.Courses.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("COURSE_CODE", $"A course already uses the code {code}.");
        }

        if (id is { } existing)
        {
            Course course = await dbContext.Courses.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(Course), existing);
            course.Update(body.Details, body.IsActive);
        }
        else
        {
            dbContext.Courses.Add(Course.Create(body.Details));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CoursesAsync(includeInactive: true, cancellationToken);
    }

    public async Task<IReadOnlyList<PathItem>> PathsAsync(CancellationToken cancellationToken)
    {
        List<LearningPath> paths = await dbContext.LearningPaths.AsNoTracking().OrderBy(row => row.Title).ToListAsync(cancellationToken);
        List<Guid> ids = [.. paths.SelectMany(path => path.CourseIds).Distinct()];
        Dictionary<Guid, Course> courses = await dbContext.Courses.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        return [.. paths.Select(path => new PathItem(
            path.Id, path.Code, path.Title, path.Description, path.CourseIds,
            [.. path.CourseIds.Where(courses.ContainsKey).Select(course => courses[course].Title)],
            path.CourseIds.Where(courses.ContainsKey).Sum(course => courses[course].DurationHours), path.IsActive))];
    }

    public async Task<IReadOnlyList<PathItem>> SavePathAsync(Guid? id, PathBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<Guid> courseIds = [.. (body.CourseIds ?? []).Distinct()];
        int found = await dbContext.Courses.CountAsync(row => courseIds.Contains(row.Id), cancellationToken);
        if (found != courseIds.Count)
        {
            throw new DomainRuleException("PATH_COURSES", "A path can only list courses in the catalogue.");
        }

        if (id is { } existing)
        {
            LearningPath path = await dbContext.LearningPaths.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(LearningPath), existing);
            path.Update(body.Code, body.Title, body.Description, courseIds, body.IsActive);
        }
        else
        {
            dbContext.LearningPaths.Add(LearningPath.Create(body.Code, body.Title, body.Description, courseIds));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PathsAsync(cancellationToken);
    }

    // ----- Enrollments --------------------------------------------------------------------------

    public async Task<IReadOnlyList<EnrollmentItem>> EnrollmentsAsync(EnrollmentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Enrollment> rows = await VisibleAsync(Permissions.Learning.EnrollmentView, cancellationToken);
        if (query.CourseId is { } course)
        {
            rows = rows.Where(row => row.CourseId == course);
        }

        if (query.EmployeeId is { } employee)
        {
            rows = rows.Where(row => row.EmployeeId == employee);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.Overdue == true)
        {
            DateOnly today = await clock.TodayAsync(cancellationToken);
            rows = rows.Where(row => (row.Status == EnrollmentStatus.Assigned || row.Status == EnrollmentStatus.InProgress) && row.DueDate < today);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<EnrollmentItem>> CertificatesAsync(bool expiringOnly, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IQueryable<Enrollment> rows = (await VisibleAsync(Permissions.Learning.CertificationView, cancellationToken)).Where(row => row.CertificateNumber != null);
        if (expiringOnly)
        {
            DateOnly soon = today.AddDays(60);
            rows = rows.Where(row => row.CertificateExpiresOn <= soon);
        }

        return await ItemsAsync(await rows.OrderBy(row => row.CertificateExpiresOn).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Assigns a course, or every course of a path, to people. Those already taking it or holding it are skipped.</summary>
    public async Task<AssignResult> AssignAsync(AssignBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<Guid> people = [.. (body.EmployeeIds ?? []).Distinct()];
        if (people.Count is 0 or > 500)
        {
            throw new DomainRuleException("ASSIGN_PEOPLE", "Assign to between 1 and 500 people at a time.");
        }

        foreach (Guid person in people)
        {
            if (!await employees.IsInScopeAsync(person, Permissions.Learning.EnrollmentManage, cancellationToken))
            {
                throw new ForbiddenException("ASSIGN_SCOPE", "You can assign learning only to people you manage.");
            }
        }

        (List<Course> courses, Guid? pathId) = await CoursesForAsync(body.CourseId, body.PathId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var created = new List<(Enrollment Enrollment, Course Course)>();
        int skipped = 0;
        foreach (Course course in courses)
        {
            HashSet<Guid> holding = await HoldingAsync(course.Id, people, today, cancellationToken);
            foreach (Guid person in people)
            {
                if (holding.Contains(person))
                {
                    skipped++;
                    continue;
                }

                DateOnly? due = body.DueDate ?? (course.CompletionDays is { } days ? today.AddDays(days) : null);
                var enrollment = Enrollment.Enroll(course.Id, person, pathId is null ? EnrollmentSource.Assigned : EnrollmentSource.Path, currentUser.EmployeeId, due, pathId);
                dbContext.Enrollments.Add(enrollment);
                created.Add((enrollment, course));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(created, cancellationToken);
        return new AssignResult(created.Count, skipped);
    }

    public async Task<EnrollmentItem> EnrollSelfAsync(Guid courseId, CancellationToken cancellationToken)
    {
        Guid me = Me();
        Course course = await dbContext.Courses.AsNoTracking().FirstOrDefaultAsync(row => row.Id == courseId && row.IsActive, cancellationToken) ?? throw new NotFoundException(nameof(Course), courseId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        if ((await HoldingAsync(courseId, [me], today, cancellationToken)).Count > 0)
        {
            throw new DomainRuleException("ENROLLMENT_EXISTS", $"You are already enrolled in, or hold, {course.Title}.");
        }

        var enrollment = Enrollment.Enroll(courseId, me, EnrollmentSource.Self, null, course.CompletionDays is { } days ? today.AddDays(days) : null, null);
        enrollment.Start(today);
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([enrollment], cancellationToken))[0];
    }

    public async Task<EnrollmentItem> ActAsync(Guid id, string action, ProgressBody? progress, CompleteBody? completion, RateBody? rating, CancellationToken cancellationToken)
    {
        Enrollment enrollment = await dbContext.Enrollments.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Enrollment), id);
        bool mine = enrollment.EmployeeId == currentUser.EmployeeId;
        if (!mine && !await ManagesAsync(enrollment.EmployeeId, cancellationToken))
        {
            throw new NotFoundException(nameof(Enrollment), id);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        Course course = await dbContext.Courses.AsNoTracking().FirstAsync(row => row.Id == enrollment.CourseId, cancellationToken);
        switch (action)
        {
            case "start":
                enrollment.Start(today);
                break;
            case "progress":
                ArgumentNullException.ThrowIfNull(progress);
                enrollment.UpdateProgress(progress.Progress, today);
                break;
            case "complete":
                DateOnly completedOn = completion?.CompletedOn ?? today;
                if (completedOn > today)
                {
                    throw new DomainRuleException("ENROLLMENT_DATE", "A course cannot be completed in the future.");
                }

                enrollment.Complete(completedOn, completion?.Score, course.CertificateValidityMonths,
                    $"{course.Code}-{completedOn.Year}-{enrollment.Id.ToString("N")[..8].ToUpperInvariant()}");
                publisher.Publish(new CourseCompletedIntegrationEvent(enrollment.Id, enrollment.EmployeeId, course.Id, course.Title, completedOn));
                break;
            case "rate":
                ArgumentNullException.ThrowIfNull(rating);
                if (!mine)
                {
                    throw new DomainRuleException("ENROLLMENT_RATING", "Only the learner rates a course.");
                }

                enrollment.Rate(rating.Rating, rating.Feedback);
                break;
            case "cancel":
                if (mine && enrollment.Source != EnrollmentSource.Self)
                {
                    throw new DomainRuleException("ENROLLMENT_ASSIGNED", "Assigned learning is cancelled by whoever assigned it.");
                }

                enrollment.Cancel();
                break;
            default:
                throw new DomainRuleException("ENROLLMENT_ACTION", $"'{action}' is not an enrollment action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([enrollment], cancellationToken))[0];
    }

    // ----- Self-service -------------------------------------------------------------------------

    public async Task<IReadOnlyList<CatalogEntry>> CatalogAsync(CancellationToken cancellationToken)
    {
        Guid? me = currentUser.EmployeeId;
        IReadOnlyList<CourseItem> courses = await CoursesAsync(includeInactive: false, cancellationToken);
        List<Enrollment> mine = me is null ? [] : await dbContext.Enrollments.AsNoTracking().Where(row => row.EmployeeId == me && row.Status != EnrollmentStatus.Cancelled).ToListAsync(cancellationToken);
        Dictionary<Guid, EnrollmentItem> latest = (await ItemsAsync(mine, cancellationToken))
            .GroupBy(row => row.CourseId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Status is EnrollmentStatus.Assigned or EnrollmentStatus.InProgress).ThenByDescending(row => row.CompletedOn).First());
        return [.. courses.Select(course => new CatalogEntry(course, latest.GetValueOrDefault(course.Id)))];
    }

    public async Task<MyLearning> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EnrollmentItem> items = await ItemsAsync(await dbContext.Enrollments.AsNoTracking().Where(row => row.EmployeeId == me && row.Status != EnrollmentStatus.Cancelled).ToListAsync(cancellationToken), cancellationToken);
        DateOnly yearStart = new(today.Year, 1, 1);
        return new MyLearning(
            [.. items.Where(row => row.Status is EnrollmentStatus.Assigned or EnrollmentStatus.InProgress).OrderBy(row => row.DueDate ?? DateOnly.MaxValue)],
            [.. items.Where(row => row.Status is EnrollmentStatus.Completed or EnrollmentStatus.Expired).OrderByDescending(row => row.CompletedOn)],
            [.. items.Where(row => row.CertificateNumber is not null).OrderBy(row => row.CertificateExpiresOn)],
            items.Where(row => row.CompletedOn >= yearStart).Sum(row => row.DurationHours));
    }

    // ----- Dashboard ----------------------------------------------------------------------------

    public async Task<LearningDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Learning.EnrollmentView, cancellationToken);
        List<Enrollment> rows = await (await VisibleAsync(Permissions.Learning.EnrollmentView, cancellationToken)).Where(row => row.Status != EnrollmentStatus.Cancelled).ToListAsync(cancellationToken);
        List<Course> mandatory = await dbContext.Courses.AsNoTracking().Where(row => row.IsActive && row.Mandatory).ToListAsync(cancellationToken);
        IReadOnlyList<Guid> people = scope is null ? await employees.ListActiveIdsAsync(null, null, cancellationToken) : [.. scope];
        decimal? compliance = null;
        if (mandatory.Count > 0 && people.Count > 0)
        {
            ILookup<Guid, Enrollment> byPerson = rows.ToLookup(row => row.EmployeeId);
            int compliant = people.Count(person => mandatory.All(course => byPerson[person].Any(row => row.CourseId == course.Id && row.Status == EnrollmentStatus.Completed)));
            compliance = Math.Round(100m * compliant / people.Count, 1);
        }

        List<Guid> top = [.. rows.GroupBy(row => row.CourseId).OrderByDescending(group => group.Count()).Take(5).Select(group => group.Key)];
        List<Course> topCourses = await dbContext.Courses.AsNoTracking().Where(row => top.Contains(row.Id)).ToListAsync(cancellationToken);
        return new LearningDashboard(
            rows.Count(row => row.Status == EnrollmentStatus.Assigned),
            rows.Count(row => row.Status == EnrollmentStatus.InProgress),
            rows.Count(row => row.Status == EnrollmentStatus.Completed && row.CompletedOn >= today.AddDays(-90)),
            rows.Count(row => row.IsOverdue(today)),
            compliance,
            rows.Count(row => row.Status == EnrollmentStatus.Completed && row.CertificateExpiresOn > today && row.CertificateExpiresOn <= today.AddDays(30)),
            rows.Count(row => row.Status == EnrollmentStatus.Expired),
            [.. (await CourseItemsAsync(topCourses, cancellationToken)).OrderByDescending(row => row.Enrolled)]);
    }

    // ----- Joiners and the daily pass -----------------------------------------------------------

    /// <summary>Gives a new joiner every active course set for joiners, due the course's completion days after joining.</summary>
    public async Task AssignJoinerAsync(Guid employeeId, DateOnly joiningDate, CancellationToken cancellationToken)
    {
        List<Course> courses = await dbContext.Courses.AsNoTracking().Where(row => row.IsActive && row.AssignToNewJoiners).ToListAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly start = joiningDate > today ? joiningDate : today;
        var created = new List<(Enrollment Enrollment, Course Course)>();
        foreach (Course course in courses)
        {
            if ((await HoldingAsync(course.Id, [employeeId], today, cancellationToken)).Count > 0)
            {
                continue;
            }

            var enrollment = Enrollment.Enroll(course.Id, employeeId, EnrollmentSource.Joiner, null, start.AddDays(course.CompletionDays ?? 30), null);
            dbContext.Enrollments.Add(enrollment);
            created.Add((enrollment, course));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(created, cancellationToken);
    }

    /// <summary>
    /// The daily pass: weekly reminders for overdue learning, a reminder 30 days before a certificate
    /// expires, and on expiry the certificate lapses - a mandatory course is assigned again. Returns the
    /// notifications sent.
    /// </summary>
    public async Task<int> DailyAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, Course> courses = await dbContext.Courses.AsNoTracking().ToDictionaryAsync(row => row.Id, cancellationToken);
        List<Enrollment> rows = await dbContext.Enrollments
            .Where(row => ((row.Status == EnrollmentStatus.Assigned || row.Status == EnrollmentStatus.InProgress) && row.DueDate < today)
                || (row.Status == EnrollmentStatus.Completed && row.CertificateExpiresOn != null && row.CertificateExpiresOn <= today.AddDays(30)))
            .ToListAsync(cancellationToken);
        int sent = 0;
        var renewals = new List<(Enrollment Enrollment, Course Course)>();
        foreach (Enrollment enrollment in rows)
        {
            Course course = courses[enrollment.CourseId];
            if (enrollment.NeedsOverdueReminder(today))
            {
                await NotifyAsync(NotificationTemplates.LearningOverdue, enrollment.EmployeeId, course, new Dictionary<string, string> { ["dueDate"] = Format(enrollment.DueDate!.Value) }, cancellationToken);
                enrollment.Reminded(today);
                sent++;
            }
            else if (enrollment.NeedsExpiryReminder(today))
            {
                await NotifyAsync(NotificationTemplates.CertificationExpiring, enrollment.EmployeeId, course, new Dictionary<string, string> { ["expiresOn"] = Format(enrollment.CertificateExpiresOn!.Value) }, cancellationToken);
                enrollment.Reminded(today);
                sent++;
            }
            else if (enrollment.Status == EnrollmentStatus.Completed && enrollment.CertificateExpiresOn <= today)
            {
                enrollment.Expire();
                if (course.Mandatory && course.IsActive && await employees.IsActiveAsync(enrollment.EmployeeId, cancellationToken)
                    && (await HoldingAsync(course.Id, [enrollment.EmployeeId], today, cancellationToken)).Count == 0)
                {
                    var renewal = Enrollment.Enroll(course.Id, enrollment.EmployeeId, EnrollmentSource.Renewal, null, today.AddDays(course.CompletionDays ?? 30), null);
                    dbContext.Enrollments.Add(renewal);
                    renewals.Add((renewal, course));
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(renewals, cancellationToken);
        return sent + renewals.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("LEARNING_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<(List<Course> Courses, Guid? PathId)> CoursesForAsync(Guid? courseId, Guid? pathId, CancellationToken cancellationToken)
    {
        if ((courseId is null) == (pathId is null))
        {
            throw new DomainRuleException("ASSIGN_WHAT", "Assign either a course or a path.");
        }

        if (courseId is { } single)
        {
            return ([await dbContext.Courses.AsNoTracking().FirstOrDefaultAsync(row => row.Id == single && row.IsActive, cancellationToken) ?? throw new NotFoundException(nameof(Course), single)], null);
        }

        LearningPath path = await dbContext.LearningPaths.AsNoTracking().FirstOrDefaultAsync(row => row.Id == pathId && row.IsActive, cancellationToken) ?? throw new NotFoundException(nameof(LearningPath), pathId!.Value);
        List<Guid> ids = [.. path.CourseIds];
        List<Course> courses = await dbContext.Courses.AsNoTracking().Where(row => ids.Contains(row.Id) && row.IsActive).ToListAsync(cancellationToken);
        return ([.. ids.Select(id => courses.Find(row => row.Id == id)).OfType<Course>()], path.Id);
    }

    /// <summary>Who of these people is already taking the course, or holds it (completed, and not lapsed).</summary>
    private async Task<HashSet<Guid>> HoldingAsync(Guid courseId, IReadOnlyCollection<Guid> people, DateOnly today, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. people];
        return [.. await dbContext.Enrollments.AsNoTracking()
            .Where(row => row.CourseId == courseId && ids.Contains(row.EmployeeId)
                && (row.Status == EnrollmentStatus.Assigned || row.Status == EnrollmentStatus.InProgress
                    || (row.Status == EnrollmentStatus.Completed && (row.CertificateExpiresOn == null || row.CertificateExpiresOn > today))))
            .Select(row => row.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken)];
    }

    private async Task NotifyAssignedAsync(IReadOnlyList<(Enrollment Enrollment, Course Course)> created, CancellationToken cancellationToken)
    {
        foreach ((Enrollment enrollment, Course course) in created)
        {
            await NotifyAsync(
                NotificationTemplates.LearningAssigned,
                enrollment.EmployeeId,
                course,
                new Dictionary<string, string> { ["dueText"] = enrollment.DueDate is { } due ? $", due {Format(due)}" : string.Empty },
                cancellationToken);
        }
    }

    private Task NotifyAsync(string template, Guid employeeId, Course course, Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        values["courseTitle"] = course.Title;
        return notifier.NotifyAsync(new NotificationRequest(template, [new NotificationRecipient(employeeId)], values, "/me/learning"), cancellationToken);
    }

    private async Task<bool> ManagesAsync(Guid employeeId, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Learning.EnrollmentManage)
            && await employees.IsInScopeAsync(employeeId, Permissions.Learning.EnrollmentManage, cancellationToken);

    private async Task<IQueryable<Enrollment>> VisibleAsync(string permission, CancellationToken cancellationToken)
    {
        IQueryable<Enrollment> rows = dbContext.Enrollments.AsNoTracking();
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(permission))
        {
            return rows.Where(row => false);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(permission, cancellationToken);
        return scope is null ? rows : rows.Where(row => scope.Contains(row.EmployeeId));
    }

    private async Task<List<CourseItem>> CourseItemsAsync(IReadOnlyList<Course> courses, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. courses.Select(row => row.Id)];
        var stats = await dbContext.Enrollments.AsNoTracking().Where(row => ids.Contains(row.CourseId) && row.Status != EnrollmentStatus.Cancelled)
            .GroupBy(row => row.CourseId)
            .Select(group => new
            {
                CourseId = group.Key,
                Enrolled = group.Count(),
                Completed = group.Count(row => row.Status == EnrollmentStatus.Completed || row.Status == EnrollmentStatus.Expired),
                Rating = group.Average(row => (decimal?)row.Rating),
            })
            .ToDictionaryAsync(row => row.CourseId, cancellationToken);
        return [.. courses.Select(row =>
        {
            var stat = stats.GetValueOrDefault(row.Id);
            return new CourseItem(
                row.Id, row.Code, row.Title, row.Description, row.Category, row.Format, row.Provider, row.DurationHours, row.ContentUrl, row.Mandatory, row.AssignToNewJoiners,
                row.CompletionDays, row.CertificateValidityMonths, row.Skills, row.IsActive, stat?.Enrolled ?? 0, stat?.Completed ?? 0,
                stat?.Rating is { } rating ? Math.Round(rating, 1) : null);
        })];
    }

    private async Task<List<EnrollmentItem>> ItemsAsync(IReadOnlyList<Enrollment> rows, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> courseIds = [.. rows.Select(row => row.CourseId).Distinct()];
        Dictionary<Guid, Course> courses = await dbContext.Courses.AsNoTracking().Where(row => courseIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(rows.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.AssignedByEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? manageScope = granted.Has(Permissions.Learning.EnrollmentManage) ? await employees.InScopeIdsAsync(Permissions.Learning.EnrollmentManage, cancellationToken) : [];
        Guid? me = currentUser.EmployeeId;
        return [.. rows.Select(row =>
        {
            Course course = courses[row.CourseId];
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new EnrollmentItem(
                row.Id, course.Id, course.Code, course.Title, course.Format, course.DurationHours, course.ContentUrl, course.Mandatory, row.EmployeeId, person?.DisplayName,
                person?.EmployeeCode, row.PathId, row.Source, row.AssignedByEmployeeId is { } by ? people.GetValueOrDefault(by)?.DisplayName : null, row.DueDate, row.Status,
                row.Progress, row.StartedOn, row.CompletedOn, row.Score, row.CertificateNumber, row.CertificateExpiresOn, row.Rating, row.IsOverdue(today),
                row.EmployeeId == me || manageScope is null || manageScope.Contains(row.EmployeeId));
        })];
    }
}

/// <summary>Seeds a tenant's default compliance courses.</summary>
public sealed class LearningSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.Courses.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        int added = 0;
        foreach (CourseDetails details in LearningDefaults.Courses.Where(course => !codes.Contains(course.Code)))
        {
            dbContext.Courses.Add(Course.Create(details));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>Completed learning for other modules.</summary>
internal sealed class LearningRecords(AppDbContext dbContext) : ILearningRecords
{
    public async Task<IReadOnlyList<LearningCompletion>> CompletionsAsync(IEnumerable<Guid> employeeIds, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. employeeIds.Distinct()];
        var rows = await (
                from enrollment in dbContext.Enrollments.AsNoTracking()
                join course in dbContext.Courses.AsNoTracking() on enrollment.CourseId equals course.Id
                where ids.Contains(enrollment.EmployeeId) && enrollment.CompletedOn != null
                    && (enrollment.Status == EnrollmentStatus.Completed || enrollment.Status == EnrollmentStatus.Expired)
                    && (firstDay == null || enrollment.CompletedOn >= firstDay) && (lastDay == null || enrollment.CompletedOn <= lastDay)
                orderby enrollment.CompletedOn descending
                select new { enrollment.EmployeeId, enrollment.CourseId, course.Code, course.Title, enrollment.CompletedOn, enrollment.CertificateExpiresOn })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new LearningCompletion(row.EmployeeId, row.CourseId, row.Code, row.Title, row.CompletedOn!.Value, row.CertificateExpiresOn))];
    }
}
