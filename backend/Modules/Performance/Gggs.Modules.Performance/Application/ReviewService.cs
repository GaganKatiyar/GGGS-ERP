using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Performance.Domain;
using Gggs.Modules.Performance.Infrastructure;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Timesheets.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Performance.Application;

public sealed record CycleItem(
    Guid Id,
    string Name,
    CycleType Type,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly SelfReviewDue,
    DateOnly ManagerReviewDue,
    IReadOnlyList<string> Competencies,
    decimal GoalWeight,
    bool PeerFeedback,
    IReadOnlyList<Guid> DepartmentIds,
    CycleStatus Status,
    DateOnly? LaunchedOn,
    DateOnly? ReleasedOn,
    int Reviews,
    IReadOnlyDictionary<string, int> ByStatus);

public sealed record ReviewItem(
    Guid Id,
    Guid CycleId,
    string CycleName,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string? Designation,
    string? DepartmentName,
    Guid? ManagerEmployeeId,
    string? ManagerName,
    ReviewStatus Status,
    int? SelfRating,
    int? ManagerRating,
    decimal? Score,
    int? CalibratedRating,
    int? FinalRating,
    string? FinalLabel,
    DateTime? SelfSubmittedDate,
    DateTime? ManagerSubmittedDate,
    DateTime? ReleasedDate);

public sealed record ReviewGoal(Guid Id, string Title, GoalStatus Status, decimal Weight, decimal Progress);

public sealed record ProjectEvidence(Guid? ProjectId, string Project, decimal Hours, string? Role);

public sealed record FeedbackItem(
    Guid Id,
    Guid ReviewId,
    Guid EmployeeId,
    string? EmployeeName,
    Guid ReviewerEmployeeId,
    string? ReviewerName,
    FeedbackRelationship Relationship,
    FeedbackStatus Status,
    IReadOnlyDictionary<string, int> Ratings,
    string? Strengths,
    string? Improvements,
    DateTime? RespondedDate,
    string CycleName,
    IReadOnlyList<string> Competencies);

public sealed record ReviewEvidence(
    IReadOnlyList<ReviewGoal> Goals,
    decimal? GoalAchievement,
    decimal? GoalRating,
    IReadOnlyList<ProjectEvidence> Projects,
    IReadOnlyList<LearningCompletion> Learning,
    IReadOnlyList<string> Skills);

public sealed record ReviewView(
    ReviewItem Review,
    CycleItem Cycle,
    ReviewInput Self,
    ReviewInput? Manager,
    string? CalibrationNote,
    string? EmployeeComment,
    ReviewEvidence Evidence,
    IReadOnlyList<FeedbackItem> Feedback,
    decimal? SuggestedScore,
    bool IsEmployee,
    bool CanEditSelf,
    bool CanEditManager,
    bool CanCalibrate,
    bool CanRequestFeedback);

public sealed record ReviewQuery
{
    public ReviewStatus? Status { get; init; }

    public Guid? DepartmentId { get; init; }
}

public sealed record ReviewSaveBody(ReviewInput Input, bool Submit);

public sealed record CalibrateBody(int Rating, string? Note);

public sealed record CommentBody(string? Comment);

public sealed record FeedbackAskBody(IReadOnlyList<Guid> ReviewerIds, FeedbackRelationship Relationship);

public sealed record FeedbackGiveBody(IReadOnlyDictionary<string, int>? Ratings, string? Strengths, string? Improvements);

public sealed record DistributionRow(int Rating, string Label, int Count, decimal Percent, int TargetPercent);

public sealed record CalibrationView(CycleItem Cycle, IReadOnlyList<ReviewItem> Reviews, IReadOnlyList<DistributionRow> Distribution, int AwaitingManager);

/// <summary>
/// Performance reviews (spec: cycles, self and manager reviews, 360 feedback, calibration, release):
/// each review weighs goal achievement and competency ratings, shows project, learning and skill
/// evidence, and ends with a calibrated rating released to the employee.
/// </summary>
internal sealed class ReviewService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ITimesheetLedger timesheets,
    IProjectDirectory projects,
    ISkillDirectory skills,
    ILearningRecords learning,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Cycles -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CycleItem>> CyclesAsync(CancellationToken cancellationToken)
        => await CycleItemsAsync(await dbContext.ReviewCycles.AsNoTracking().OrderByDescending(row => row.PeriodEnd).ToListAsync(cancellationToken), cancellationToken);

    public async Task<CycleItem> CycleAsync(Guid id, CancellationToken cancellationToken)
        => (await CycleItemsAsync([await dbContext.ReviewCycles.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ReviewCycle), id)], cancellationToken))[0];

    public async Task<CycleItem> SaveCycleAsync(Guid? id, CycleDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        ReviewCycle cycle;
        if (id is { } existing)
        {
            cycle = await dbContext.ReviewCycles.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(ReviewCycle), existing);
            cycle.Update(details);
        }
        else
        {
            cycle = ReviewCycle.Draft(details);
            dbContext.ReviewCycles.Add(cycle);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CycleAsync(cycle.Id, cancellationToken);
    }

    /// <summary>Opens a review for every active employee in the cycle's departments (everyone when none), with their manager as reviewer.</summary>
    public async Task<CycleItem> LaunchAsync(Guid id, CancellationToken cancellationToken)
    {
        ReviewCycle cycle = await dbContext.ReviewCycles.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ReviewCycle), id);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        cycle.Launch(today);
        IReadOnlyList<Guid> departments = cycle.DepartmentIds;
        IReadOnlyList<Guid> people = await employees.ListActiveIdsAsync(departments.Count == 0 ? null : [.. departments], null, cancellationToken);
        IReadOnlyList<EmployeeSummary> summaries = await employees.GetManyAsync(people, cancellationToken);
        List<EmployeeSummary> joined = [.. summaries.Where(person => person.JoiningDate is null || person.JoiningDate <= cycle.PeriodEnd)];
        if (joined.Count == 0)
        {
            throw new DomainRuleException("CYCLE_EMPTY", "Nobody is in scope for this cycle.");
        }

        foreach (EmployeeSummary person in joined)
        {
            dbContext.Reviews.Add(Review.Open(cycle.Id, person.Id, person.ManagerEmployeeId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.ReviewCycleLaunched,
                [.. joined.Select(person => new NotificationRecipient(person.Id))],
                new Dictionary<string, string> { ["cycleName"] = cycle.Name, ["dueDate"] = Format(cycle.SelfReviewDue) },
                "/me/reviews"),
            cancellationToken);
        return await CycleAsync(id, cancellationToken);
    }

    public async Task<CycleItem> CycleActionAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        ReviewCycle cycle = await dbContext.ReviewCycles.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ReviewCycle), id);
        switch (action)
        {
            case "calibrate":
                cycle.StartCalibration();
                break;
            case "release":
                await ReleaseAsync(cycle, cancellationToken);
                break;
            case "close":
                cycle.Close();
                break;
            default:
                throw new DomainRuleException("CYCLE_ACTION", $"'{action}' is not a cycle action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CycleAsync(id, cancellationToken);
    }

    public async Task<CalibrationView> CalibrationAsync(Guid id, Guid? departmentId, CancellationToken cancellationToken)
    {
        CycleItem cycle = await CycleAsync(id, cancellationToken);
        List<Review> reviews = await dbContext.Reviews.AsNoTracking().Where(row => row.CycleId == id).ToListAsync(cancellationToken);
        List<ReviewItem> items = await ItemsAsync(reviews, cancellationToken);
        if (departmentId is { } department)
        {
            Dictionary<Guid, Guid?> departments = (await employees.GetManyAsync(items.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id, person => person.DepartmentId);
            items = [.. items.Where(row => departments.GetValueOrDefault(row.EmployeeId) == department)];
        }

        List<ReviewItem> rated = [.. items.Where(row => row.Status >= ReviewStatus.ManagerSubmitted)];
        List<DistributionRow> distribution = [.. Enumerable.Range(RatingScale.Min, RatingScale.Max).Select(rating =>
        {
            int count = rated.Count(row => (row.FinalRating ?? row.CalibratedRating ?? row.ManagerRating) == rating);
            return new DistributionRow(rating, RatingScale.Label(rating), count, rated.Count == 0 ? 0 : Math.Round(100m * count / rated.Count, 1), RatingScale.DefaultDistribution[rating]);
        })];
        return new CalibrationView(cycle, [.. items.OrderBy(row => row.DepartmentName, StringComparer.CurrentCulture).ThenBy(row => row.EmployeeName, StringComparer.CurrentCulture)], distribution, items.Count(row => row.Status < ReviewStatus.ManagerSubmitted));
    }

    // ----- Reviews ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ReviewItem>> ReviewsAsync(Guid cycleId, ReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Review> rows = (await VisibleAsync(cancellationToken)).Where(row => row.CycleId == cycleId);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        List<ReviewItem> items = await ItemsAsync(await rows.ToListAsync(cancellationToken), cancellationToken);
        if (query.DepartmentId is { } department)
        {
            Dictionary<Guid, Guid?> departments = (await employees.GetManyAsync(items.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id, person => person.DepartmentId);
            items = [.. items.Where(row => departments.GetValueOrDefault(row.EmployeeId) == department)];
        }

        return [.. items.OrderBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
    }

    /// <summary>My own reviews, and the reviews I am the manager on.</summary>
    public async Task<IReadOnlyList<ReviewItem>> MineAsync(bool asManager, CancellationToken cancellationToken)
    {
        Guid me = Me();
        List<Review> rows = await dbContext.Reviews.AsNoTracking()
            .Where(row => asManager ? row.ManagerEmployeeId == me : row.EmployeeId == me)
            .OrderByDescending(row => row.CreatedDate)
            .Take(300)
            .ToListAsync(cancellationToken);
        return await ItemsAsync(rows, cancellationToken);
    }

    public async Task<ReviewView> ReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        Review review = await VisibleReviewAsync(id, cancellationToken);
        ReviewCycle cycle = await dbContext.ReviewCycles.AsNoTracking().FirstAsync(row => row.Id == review.CycleId, cancellationToken);
        bool isEmployee = review.EmployeeId == currentUser.EmployeeId;
        bool canManage = await CanManageAsync(review, cancellationToken);
        bool canCalibrate = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Performance.CalibrationManage);
        bool released = review.Status >= ReviewStatus.Released;
        bool showManager = !isEmployee || released || canManage;
        ReviewEvidence evidence = await EvidenceAsync(review, cycle, cancellationToken);
        List<FeedbackRequest> feedback = await dbContext.FeedbackRequests.AsNoTracking().Where(row => row.ReviewId == id).ToListAsync(cancellationToken);

        // The employee sees feedback only once released, and without who gave it.
        List<FeedbackItem> feedbackItems = isEmployee && !canManage
            ? released
                ? [.. (await FeedbackItemsAsync([.. feedback.Where(row => row.Status == FeedbackStatus.Submitted)], cancellationToken)).Select(row => row with { ReviewerEmployeeId = Guid.Empty, ReviewerName = null })]
                : []
            : await FeedbackItemsAsync(feedback, cancellationToken);
        ReviewInput manager = review.Manager;
        decimal? suggested = RatingScale.Score(evidence.GoalRating, manager.Ratings ?? new Dictionary<string, int>(), cycle.GoalWeight);
        bool running = cycle.Status is CycleStatus.Active or CycleStatus.Calibration;
        return new ReviewView(
            (await ItemsAsync([review], cancellationToken))[0],
            (await CycleItemsAsync([cycle], cancellationToken))[0],
            review.Self,
            showManager ? manager : null,
            canCalibrate || released ? review.CalibrationNote : null,
            review.EmployeeComment,
            evidence,
            feedbackItems,
            canManage ? suggested : null,
            isEmployee,
            isEmployee && running && review.Status == ReviewStatus.Pending,
            canManage && running && review.Status is ReviewStatus.Pending or ReviewStatus.SelfSubmitted,
            canCalibrate && running && review.Status == ReviewStatus.ManagerSubmitted,
            (isEmployee || canManage) && running && cycle.PeerFeedback && review.Status < ReviewStatus.ManagerSubmitted);
    }

    public async Task<ReviewView> SaveSelfAsync(Guid id, ReviewSaveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Review review = await dbContext.Reviews.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);
        if (review.EmployeeId != currentUser.EmployeeId)
        {
            throw new NotFoundException(nameof(Review), id);
        }

        ReviewCycle cycle = await RunningCycleAsync(review.CycleId, cancellationToken);
        EnsureCompetencies(cycle, body.Input.Ratings);
        review.SaveSelf(body.Input, body.Submit, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit && review.ManagerEmployeeId is { } manager)
        {
            string name = (await employees.GetAsync(review.EmployeeId, cancellationToken))?.DisplayName ?? "Your report";
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ReviewSubmitted,
                    [new NotificationRecipient(manager)],
                    new Dictionary<string, string> { ["employeeName"] = name, ["cycleName"] = cycle.Name },
                    $"/performance/reviews/{review.Id}"),
                cancellationToken);
        }

        return await ReviewAsync(id, cancellationToken);
    }

    public async Task<ReviewView> SaveManagerAsync(Guid id, ReviewSaveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Review review = await dbContext.Reviews.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);
        if (!await CanManageAsync(review, cancellationToken))
        {
            throw new NotFoundException(nameof(Review), id);
        }

        ReviewCycle cycle = await RunningCycleAsync(review.CycleId, cancellationToken);
        EnsureCompetencies(cycle, body.Input.Ratings);
        ReviewEvidence evidence = await EvidenceAsync(review, cycle, cancellationToken);
        decimal? score = RatingScale.Score(evidence.GoalRating, body.Input.Ratings ?? new Dictionary<string, int>(), cycle.GoalWeight);
        review.SaveManager(body.Input, body.Submit, evidence.GoalAchievement, score, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ReviewAsync(id, cancellationToken);
    }

    public async Task<ReviewView> ReturnAsync(Guid id, CancellationToken cancellationToken)
    {
        Review review = await dbContext.Reviews.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);
        await RunningCycleAsync(review.CycleId, cancellationToken);
        review.ReturnToManager();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ReviewAsync(id, cancellationToken);
    }

    public async Task<ReviewView> CalibrateAsync(Guid id, CalibrateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Review review = await dbContext.Reviews.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);
        await RunningCycleAsync(review.CycleId, cancellationToken);
        review.Calibrate(body.Rating, body.Note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ReviewAsync(id, cancellationToken);
    }

    public async Task<ReviewView> AcknowledgeAsync(Guid id, string? comment, CancellationToken cancellationToken)
    {
        Review review = await dbContext.Reviews.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);
        if (review.EmployeeId != currentUser.EmployeeId)
        {
            throw new NotFoundException(nameof(Review), id);
        }

        review.Acknowledge(comment, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ReviewAsync(id, cancellationToken);
    }

    // ----- 360 feedback -------------------------------------------------------------------------

    public async Task<ReviewView> AskFeedbackAsync(Guid reviewId, FeedbackAskBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Review review = await VisibleReviewAsync(reviewId, cancellationToken);
        if (review.EmployeeId != currentUser.EmployeeId && !await CanManageAsync(review, cancellationToken))
        {
            throw new ForbiddenException("FEEDBACK_FORBIDDEN", "Feedback is asked for by the person reviewed or their manager.");
        }

        ReviewCycle cycle = await RunningCycleAsync(review.CycleId, cancellationToken);
        if (!cycle.PeerFeedback)
        {
            throw new DomainRuleException("FEEDBACK_OFF", "This cycle does not collect 360 feedback.");
        }

        HashSet<Guid> asked = [.. await dbContext.FeedbackRequests.Where(row => row.ReviewId == reviewId).Select(row => row.ReviewerEmployeeId).ToListAsync(cancellationToken)];
        List<Guid> reviewers = [.. (body.ReviewerIds ?? []).Distinct().Where(reviewer => !asked.Contains(reviewer))];
        if (reviewers.Count + asked.Count > 10)
        {
            throw new DomainRuleException("FEEDBACK_LIMIT", "Ask up to ten people for feedback on one review.");
        }

        foreach (Guid reviewer in reviewers)
        {
            if (!await employees.IsActiveAsync(reviewer, cancellationToken))
            {
                throw new DomainRuleException("FEEDBACK_REVIEWER", "Feedback can only be asked of active employees.");
            }

            dbContext.FeedbackRequests.Add(FeedbackRequest.Ask(reviewId, review.EmployeeId, reviewer, currentUser.EmployeeId, body.Relationship));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (reviewers.Count > 0)
        {
            Dictionary<Guid, string> names = await NamesAsync([review.EmployeeId, currentUser.EmployeeId], cancellationToken);
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.FeedbackRequested,
                    [.. reviewers.Select(reviewer => new NotificationRecipient(reviewer))],
                    new Dictionary<string, string>
                    {
                        ["requesterName"] = currentUser.EmployeeId is { } me ? names.GetValueOrDefault(me) ?? "A colleague" : "HR",
                        ["employeeName"] = names.GetValueOrDefault(review.EmployeeId) ?? "a colleague",
                        ["cycleName"] = cycle.Name,
                    },
                    "/me/feedback"),
                cancellationToken);
        }

        return await ReviewAsync(reviewId, cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackItem>> MyFeedbackAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        List<FeedbackRequest> rows = await dbContext.FeedbackRequests.AsNoTracking().Where(row => row.ReviewerEmployeeId == me).OrderBy(row => row.Status).ThenByDescending(row => row.CreatedDate).Take(200).ToListAsync(cancellationToken);
        return await FeedbackItemsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackItem>> GiveFeedbackAsync(Guid id, FeedbackGiveBody? body, bool decline, CancellationToken cancellationToken)
    {
        Guid me = Me();
        FeedbackRequest request = await dbContext.FeedbackRequests.FirstOrDefaultAsync(row => row.Id == id && row.ReviewerEmployeeId == me, cancellationToken) ?? throw new NotFoundException(nameof(FeedbackRequest), id);
        Review review = await dbContext.Reviews.AsNoTracking().FirstAsync(row => row.Id == request.ReviewId, cancellationToken);
        await RunningCycleAsync(review.CycleId, cancellationToken);
        if (decline)
        {
            request.Decline(Now);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(body);
            request.Submit(body.Ratings, body.Strengths, body.Improvements, Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await MyFeedbackAsync(cancellationToken);
    }

    // ----- Jobs ---------------------------------------------------------------------------------

    /// <summary>Reminds people whose self or manager review is due within three days, or overdue. Returns the reminders sent.</summary>
    public async Task<int> RemindAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ReviewCycle> cycles = await dbContext.ReviewCycles.AsNoTracking().Where(row => row.Status == CycleStatus.Active).ToListAsync(cancellationToken);
        int sent = 0;
        foreach (ReviewCycle cycle in cycles)
        {
            List<Review> reviews = await dbContext.Reviews.Where(row => row.CycleId == cycle.Id && (row.Status == ReviewStatus.Pending || row.Status == ReviewStatus.SelfSubmitted)).ToListAsync(cancellationToken);
            Dictionary<Guid, string> names = await NamesAsync(reviews.Select(row => (Guid?)row.EmployeeId), cancellationToken);
            foreach (Review review in reviews)
            {
                bool self = review.Status == ReviewStatus.Pending;
                DateOnly due = self ? cycle.SelfReviewDue : cycle.ManagerReviewDue;
                Guid? recipient = self ? review.EmployeeId : review.ManagerEmployeeId;
                if (recipient is null || !review.NeedsReminder(today, due))
                {
                    continue;
                }

                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.ReviewDue,
                        [new NotificationRecipient(recipient)],
                        new Dictionary<string, string> { ["employeeName"] = self ? "your self-review" : names.GetValueOrDefault(review.EmployeeId) ?? "your report", ["cycleName"] = cycle.Name, ["dueDate"] = Format(due) },
                        self ? "/me/reviews" : $"/performance/reviews/{review.Id}"),
                    cancellationToken);
                review.Reminded(today);
                sent++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("REVIEW_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private static void EnsureCompetencies(ReviewCycle cycle, IReadOnlyDictionary<string, int>? ratings)
    {
        HashSet<string> known = new(cycle.Competencies, StringComparer.OrdinalIgnoreCase);
        if (ratings is not null && ratings.Keys.Any(key => !known.Contains(key)))
        {
            throw new DomainRuleException("REVIEW_COMPETENCY", "Rate only the cycle's competencies.");
        }
    }

    /// <summary>Releases every manager-submitted review in the cycle; the rest wait for their manager.</summary>
    private async Task ReleaseAsync(ReviewCycle cycle, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Review> ready = await dbContext.Reviews.Where(row => row.CycleId == cycle.Id && row.Status == ReviewStatus.ManagerSubmitted).ToListAsync(cancellationToken);
        if (ready.Count == 0)
        {
            throw new DomainRuleException("CYCLE_NOTHING", "No manager review is ready to release.");
        }

        cycle.MarkReleased(today);
        foreach (Review review in ready)
        {
            review.Release(Now);
            publisher.Publish(new ReviewReleasedIntegrationEvent(review.Id, cycle.Id, review.EmployeeId, review.FinalRating ?? 0));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.ReviewReleased,
                [.. ready.Select(review => new NotificationRecipient(review.EmployeeId))],
                new Dictionary<string, string> { ["cycleName"] = cycle.Name },
                "/me/reviews"),
            cancellationToken);
    }

    private async Task<ReviewCycle> RunningCycleAsync(Guid cycleId, CancellationToken cancellationToken)
    {
        ReviewCycle cycle = await dbContext.ReviewCycles.AsNoTracking().FirstAsync(row => row.Id == cycleId, cancellationToken);
        cycle.EnsureRunning();
        return cycle;
    }

    /// <summary>The goals in the review's period, project hours from approved timesheets, learning completed and current skills.</summary>
    private async Task<ReviewEvidence> EvidenceAsync(Review review, ReviewCycle cycle, CancellationToken cancellationToken)
    {
        List<Goal> goals = await dbContext.Goals.AsNoTracking()
            .Where(row => row.OwnerEmployeeId == review.EmployeeId && row.Level == GoalLevel.Individual && row.Status != GoalStatus.Cancelled && row.Status != GoalStatus.Draft
                && (row.CycleId == cycle.Id || (row.CycleId == null && row.StartDate <= cycle.PeriodEnd && row.DueDate >= cycle.PeriodStart)))
            .OrderBy(row => row.DueDate)
            .ToListAsync(cancellationToken);
        decimal? achievement = RatingScale.GoalAchievement([.. goals.Select(goal => (goal.Progress, goal.Weight))]);

        IReadOnlyList<LoggedHours> hours = await timesheets.HoursAsync([review.EmployeeId], cycle.PeriodStart, cycle.PeriodEnd, approvedOnly: true, cancellationToken);
        List<Guid> projectIds = [.. hours.Where(row => row.ProjectId != null).Select(row => row.ProjectId!.Value).Distinct()];
        IReadOnlyDictionary<Guid, ProjectSummary> names = await projects.GetManyAsync(projectIds, cancellationToken);
        Dictionary<Guid, string> roles = (await projects.MembershipsAsync(review.EmployeeId, cancellationToken))
            .GroupBy(row => row.ProjectId).ToDictionary(group => group.Key, group => group.First().RoleName);
        List<ProjectEvidence> projectEvidence = [.. hours
            .GroupBy(row => row.ProjectId)
            .Select(group => new ProjectEvidence(
                group.Key,
                group.Key is { } project ? names.TryGetValue(project, out ProjectSummary? summary) ? $"{summary.Code} · {summary.Name}" : "Project" : "Non-project time",
                group.Sum(row => row.Hours),
                group.Key is { } key ? roles.GetValueOrDefault(key) : null))
            .OrderByDescending(row => row.Hours)];

        IReadOnlyList<LearningCompletion> completions = await learning.CompletionsAsync([review.EmployeeId], cycle.PeriodStart, cycle.PeriodEnd, cancellationToken);
        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync([review.EmployeeId], cancellationToken);
        return new ReviewEvidence(
            [.. goals.Select(goal => new ReviewGoal(goal.Id, goal.Title, goal.Status, goal.Weight, goal.Progress))],
            achievement,
            achievement is { } value ? RatingScale.FromProgress(value) : null,
            projectEvidence,
            completions,
            [.. levels.OrderByDescending(level => level.Level).Select(level => $"{level.SkillName} ({level.Level}/5)")]);
    }

    /// <summary>One's own reviews, the reviews one manages, and those of people in review scope.</summary>
    private async Task<IQueryable<Review>> VisibleAsync(CancellationToken cancellationToken)
    {
        IQueryable<Review> rows = dbContext.Reviews.AsNoTracking();
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = granted.Has(Permissions.Performance.ReviewView) ? await employees.InScopeIdsAsync(Permissions.Performance.ReviewView, cancellationToken) : [];
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.EmployeeId == me || row.ManagerEmployeeId == me || scope.Contains(row.EmployeeId));
    }

    private async Task<Review> VisibleReviewAsync(Guid id, CancellationToken cancellationToken)
        => await (await VisibleAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Review), id);

    /// <summary>The manager on the review, or someone who manages reviews for the employee (HR, a skip-level).</summary>
    private async Task<bool> CanManageAsync(Review review, CancellationToken cancellationToken)
    {
        if (review.EmployeeId == currentUser.EmployeeId)
        {
            return false;
        }

        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        if (!granted.Has(Permissions.Performance.ReviewManageAsManager))
        {
            return false;
        }

        return review.ManagerEmployeeId == currentUser.EmployeeId || await employees.IsInScopeAsync(review.EmployeeId, Permissions.Performance.ReviewManageAsManager, cancellationToken);
    }

    private async Task<List<CycleItem>> CycleItemsAsync(IReadOnlyList<ReviewCycle> cycles, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. cycles.Select(row => row.Id)];
        var counts = await dbContext.Reviews.AsNoTracking().Where(row => ids.Contains(row.CycleId))
            .GroupBy(row => new { row.CycleId, row.Status })
            .Select(group => new { group.Key.CycleId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return [.. cycles.Select(row => new CycleItem(
            row.Id, row.Name, row.Type, row.PeriodStart, row.PeriodEnd, row.SelfReviewDue, row.ManagerReviewDue, row.Competencies, row.GoalWeight, row.PeerFeedback,
            row.DepartmentIds, row.Status, row.LaunchedOn, row.ReleasedOn, counts.Where(count => count.CycleId == row.Id).Sum(count => count.Count),
            counts.Where(count => count.CycleId == row.Id).ToDictionary(count => count.Status.ToString(), count => count.Count)))];
    }

    private async Task<List<ReviewItem>> ItemsAsync(IReadOnlyList<Review> reviews, CancellationToken cancellationToken)
    {
        List<Guid> cycleIds = [.. reviews.Select(row => row.CycleId).Distinct()];
        Dictionary<Guid, string> cycles = await dbContext.ReviewCycles.AsNoTracking().Where(row => cycleIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(reviews.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.ManagerEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(
            OrgUnitKind.Department, people.Values.Where(person => person.DepartmentId != null).Select(person => person.DepartmentId!.Value).Distinct(), cancellationToken);
        return [.. reviews.Select(row =>
        {
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new ReviewItem(
                row.Id, row.CycleId, cycles.GetValueOrDefault(row.CycleId) ?? string.Empty, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, person?.Designation,
                person?.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null, row.ManagerEmployeeId,
                row.ManagerEmployeeId is { } manager ? people.GetValueOrDefault(manager)?.DisplayName : null, row.Status, row.Self.Overall, row.ManagerRating, row.Score,
                row.CalibratedRating, row.FinalRating, row.FinalRating is { } final ? RatingScale.Label(final) : null, row.SelfSubmittedDate, row.ManagerSubmittedDate, row.ReleasedDate);
        })];
    }

    private async Task<List<FeedbackItem>> FeedbackItemsAsync(IReadOnlyList<FeedbackRequest> rows, CancellationToken cancellationToken)
    {
        List<Guid> reviewIds = [.. rows.Select(row => row.ReviewId).Distinct()];
        var reviews = await dbContext.Reviews.AsNoTracking().Where(row => reviewIds.Contains(row.Id)).Select(row => new { row.Id, row.CycleId }).ToListAsync(cancellationToken);
        List<Guid> cycleIds = [.. reviews.Select(row => row.CycleId).Distinct()];
        Dictionary<Guid, ReviewCycle> cycles = await dbContext.ReviewCycles.AsNoTracking().Where(row => cycleIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, Guid> cycleOf = reviews.ToDictionary(row => row.Id, row => row.CycleId);
        Dictionary<Guid, string> names = await NamesAsync(rows.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.ReviewerEmployeeId }), cancellationToken);
        return [.. rows.Select(row =>
        {
            ReviewCycle? cycle = cycleOf.TryGetValue(row.ReviewId, out Guid cycleId) ? cycles.GetValueOrDefault(cycleId) : null;
            return new FeedbackItem(
                row.Id, row.ReviewId, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.ReviewerEmployeeId, names.GetValueOrDefault(row.ReviewerEmployeeId),
                row.Relationship, row.Status, row.Ratings, row.Strengths, row.Improvements, row.RespondedDate, cycle?.Name ?? string.Empty, cycle?.Competencies ?? []);
        })];
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
}

/// <summary>Released ratings for other modules.</summary>
internal sealed class PerformanceRatings(AppDbContext dbContext) : IPerformanceRatings
{
    public async Task<IReadOnlyDictionary<Guid, RatingInfo>> LatestAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. employeeIds.Distinct()];
        var rows = await (
                from review in dbContext.Reviews.AsNoTracking()
                join cycle in dbContext.ReviewCycles.AsNoTracking() on review.CycleId equals cycle.Id
                where ids.Contains(review.EmployeeId) && review.FinalRating != null && review.ReleasedDate != null
                select new { review.EmployeeId, review.CycleId, cycle.Name, review.FinalRating, review.ReleasedDate })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.EmployeeId)
            .Select(group => group.OrderByDescending(row => row.ReleasedDate).First())
            .ToDictionary(
                row => row.EmployeeId,
                row => new RatingInfo(row.EmployeeId, row.CycleId, row.Name, row.FinalRating!.Value, RatingScale.Label(row.FinalRating.Value), DateOnly.FromDateTime(row.ReleasedDate!.Value)));
    }
}
