using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Career.Domain;
using Gggs.Modules.Learning.Domain;
using Gggs.Modules.Performance.Domain;
using Shouldly;

namespace Gggs.Talent.UnitTests;

public sealed class GoalTests
{
    private static readonly DateOnly Start = new(2026, 4, 1);

    private static GoalDetails Individual(decimal weight = 50, Guid? owner = null) =>
        new("Ship billing", null, GoalType.Okr, GoalLevel.Individual, owner ?? Guid.NewGuid(), null, null, null, Start, Start.AddMonths(6), weight);

    [Theory]
    [InlineData(0, 10, 5, 50)]
    [InlineData(100, 60, 80, 50)]
    [InlineData(0, 10, 15, 100)]
    [InlineData(0, 10, -3, 0)]
    public void A_key_result_measures_progress_towards_its_target_in_either_direction(double start, double target, double current, double expected)
        => KeyResult.Measure(MetricType.Number, (decimal)start, (decimal)target, (decimal)current).ShouldBe((decimal)expected);

    [Fact]
    public void A_milestone_is_done_or_not()
    {
        KeyResult.Measure(MetricType.Milestone, 0, 1, 0).ShouldBe(0);
        KeyResult.Measure(MetricType.Milestone, 0, 1, 1).ShouldBe(100);
    }

    [Fact]
    public void Key_results_roll_up_by_their_weights()
    {
        Guid goal = Guid.NewGuid();
        var half = KeyResult.Add(goal, new KeyResultDetails("Endpoints", MetricType.Number, 0, 10, null, 75), 0);
        half.Record(10);
        var none = KeyResult.Add(goal, new KeyResultDetails("Review", MetricType.Milestone, 0, 1, null, 25), 1);
        KeyResult.Rollup([half, none]).ShouldBe(75);
    }

    [Fact]
    public void A_key_result_needs_a_target_that_moves()
        => Should.Throw<DomainRuleException>(() => KeyResult.Add(Guid.NewGuid(), new KeyResultDetails("Flat", MetricType.Number, 5, 5, null, 10), 0)).ErrorCode.ShouldBe("KEY_RESULT_TARGET");

    [Fact]
    public void Goals_validate_their_level_dates_and_weight()
    {
        Should.Throw<DomainRuleException>(() => Goal.Create(Individual() with { OwnerEmployeeId = null }, true)).ErrorCode.ShouldBe("GOAL_OWNER");
        Should.Throw<DomainRuleException>(() => Goal.Create(Individual() with { Level = GoalLevel.Department }, true)).ErrorCode.ShouldBe("GOAL_DEPARTMENT");
        Should.Throw<DomainRuleException>(() => Goal.Create(Individual() with { DueDate = Start.AddDays(-1) }, true)).ErrorCode.ShouldBe("GOAL_DATES");
        Should.Throw<DomainRuleException>(() => Goal.Create(Individual(weight: 120), true)).ErrorCode.ShouldBe("GOAL_WEIGHT");
    }

    [Fact]
    public void A_check_in_activates_a_draft_and_a_completed_goal_must_be_reopened_to_change()
    {
        var goal = Goal.Create(Individual(), activate: false);
        goal.Record(140, Confidence.OnTrack, Start.AddDays(10));
        goal.Status.ShouldBe(GoalStatus.Active);
        goal.Progress.ShouldBe(100);

        goal.Complete(Start.AddMonths(2));
        Should.Throw<DomainRuleException>(() => goal.Update(Individual()));
        goal.Reopen();
        goal.CompletedOn.ShouldBeNull();
        goal.Cancel();
        Should.Throw<DomainRuleException>(() => goal.Record(10, null, Start));
    }
}

public sealed class RatingScaleTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 3)]
    [InlineData(75, 4)]
    [InlineData(100, 5)]
    public void Goal_achievement_maps_onto_the_five_point_scale(double progress, double rating)
        => RatingScale.FromProgress((decimal)progress).ShouldBe((decimal)rating);

    [Fact]
    public void A_score_blends_goals_and_competencies_by_the_goal_weight()
    {
        var competencies = new Dictionary<string, int> { ["Delivery"] = 3, ["Collaboration"] = 5 };
        RatingScale.Score(4, competencies, 50).ShouldBe(4);
        RatingScale.Score(5, competencies, 25).ShouldBe(4.25m);
        RatingScale.Score(null, competencies, 50).ShouldBe(4);
        RatingScale.Score(2, new Dictionary<string, int>(), 50).ShouldBe(2);
        RatingScale.Score(null, new Dictionary<string, int>(), 50).ShouldBeNull();
    }

    [Fact]
    public void Goal_achievement_is_weighted_or_averaged()
    {
        RatingScale.GoalAchievement([(100m, 60m), (50m, 40m)]).ShouldBe(80);
        RatingScale.GoalAchievement([(100m, 0m), (50m, 0m)]).ShouldBe(75);
        RatingScale.GoalAchievement([]).ShouldBeNull();
    }

    [Fact]
    public void The_default_distribution_adds_up_to_everyone()
        => RatingScale.DefaultDistribution.Values.Sum().ShouldBe(100);
}

public sealed class ReviewTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CycleDetails Cycle(decimal goalWeight = 50, string[]? competencies = null) => new(
        "H2", CycleType.HalfYearly, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 20),
        competencies ?? ["Delivery", "delivery", " Collaboration "], goalWeight, true, null);

    [Fact]
    public void A_cycle_needs_competencies_unless_goals_weigh_everything_and_keeps_its_period_once_launched()
    {
        var cycle = ReviewCycle.Draft(Cycle());
        cycle.Competencies.ShouldBe(["Delivery", "Collaboration"]);
        Should.Throw<DomainRuleException>(() => ReviewCycle.Draft(Cycle(competencies: []))).ErrorCode.ShouldBe("CYCLE_COMPETENCIES");
        ReviewCycle.Draft(Cycle(goalWeight: 100, competencies: [])).GoalWeight.ShouldBe(100);

        cycle.Launch(new DateOnly(2026, 10, 1));
        Should.Throw<DomainRuleException>(() => cycle.Update(Cycle() with { PeriodStart = new DateOnly(2026, 1, 1) }));
        cycle.Update(Cycle() with { SelfReviewDue = new DateOnly(2026, 10, 12) });
        Should.Throw<DomainRuleException>(cycle.Close);
        cycle.StartCalibration();
        cycle.MarkReleased(new DateOnly(2026, 11, 1));
        cycle.Close();
    }

    [Fact]
    public void A_review_moves_from_self_to_manager_to_calibration_and_release()
    {
        var review = Review.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        review.SaveSelf(new ReviewInput(new Dictionary<string, int> { ["Delivery"] = 4 }, "Shipped", null, null, 4), submit: false, Now);
        review.Status.ShouldBe(ReviewStatus.Pending);
        review.SaveSelf(new ReviewInput(new Dictionary<string, int> { ["Delivery"] = 4 }, "Shipped", null, null, 4), submit: true, Now);
        Should.Throw<DomainRuleException>(() => review.SaveSelf(new ReviewInput(null, null, null, null, null), true, Now));

        Should.Throw<DomainRuleException>(() => review.SaveManager(new ReviewInput(null, null, null, null, null), submit: true, null, null, Now)).ErrorCode.ShouldBe("REVIEW_RATING");
        review.SaveManager(new ReviewInput(new Dictionary<string, int> { ["Delivery"] = 3 }, null, null, "Good", 4), submit: true, 80, 3.6m, Now);
        review.ManagerRating.ShouldBe(4);

        review.ReturnToManager();
        review.Status.ShouldBe(ReviewStatus.SelfSubmitted);
        review.SaveManager(new ReviewInput(new Dictionary<string, int> { ["Delivery"] = 3 }, null, null, "Good", 4), submit: true, 80, 3.6m, Now);
        review.Calibrate(3, "Aligned");
        review.Release(Now);
        review.FinalRating.ShouldBe(3);
        review.Acknowledge("Thanks", Now);
        review.Status.ShouldBe(ReviewStatus.Acknowledged);
    }

    [Fact]
    public void Without_calibration_the_manager_rating_is_final_and_ratings_stay_on_the_scale()
    {
        var review = Review.Open(Guid.NewGuid(), Guid.NewGuid(), null);
        Should.Throw<DomainRuleException>(() => review.SaveSelf(new ReviewInput(new Dictionary<string, int> { ["Delivery"] = 6 }, null, null, null, null), true, Now));
        review.SaveManager(new ReviewInput(null, null, null, null, 5), submit: true, null, null, Now);
        review.Release(Now);
        review.FinalRating.ShouldBe(5);
    }

    [Fact]
    public void A_review_is_reminded_from_three_days_before_it_is_due_then_every_three_days()
    {
        var review = Review.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var due = new DateOnly(2026, 10, 10);
        review.NeedsReminder(due.AddDays(-4), due).ShouldBeFalse();
        review.NeedsReminder(due.AddDays(-3), due).ShouldBeTrue();
        review.Reminded(due.AddDays(-3));
        review.NeedsReminder(due.AddDays(-1), due).ShouldBeFalse();
        review.NeedsReminder(due, due).ShouldBeTrue();
    }

    [Fact]
    public void Feedback_is_asked_of_someone_else_and_given_once()
    {
        Guid employee = Guid.NewGuid();
        Should.Throw<DomainRuleException>(() => FeedbackRequest.Ask(Guid.NewGuid(), employee, employee, null, FeedbackRelationship.Peer)).ErrorCode.ShouldBe("FEEDBACK_SELF");
        var request = FeedbackRequest.Ask(Guid.NewGuid(), employee, Guid.NewGuid(), null, FeedbackRelationship.Peer);
        Should.Throw<DomainRuleException>(() => request.Submit(null, " ", null, Now)).ErrorCode.ShouldBe("FEEDBACK_EMPTY");
        request.Submit(new Dictionary<string, int> { ["Delivery"] = 5 }, "Reliable", null, Now);
        Should.Throw<DomainRuleException>(() => request.Decline(Now));
    }

    [Fact]
    public void An_improvement_plan_has_objectives_a_bounded_length_and_an_outcome()
    {
        var start = new DateOnly(2026, 10, 1);
        Should.Throw<DomainRuleException>(() => ImprovementPlan.Draft(Guid.NewGuid(), null, new PipDetails("Missed dates", start, start.AddDays(200), [new PipObjective("Deliver", null, null)], null))).ErrorCode.ShouldBe("PIP_DATES");
        Should.Throw<DomainRuleException>(() => ImprovementPlan.Draft(Guid.NewGuid(), null, new PipDetails("Missed dates", start, start.AddDays(60), [], null))).ErrorCode.ShouldBe("PIP_OBJECTIVES");

        var plan = ImprovementPlan.Draft(Guid.NewGuid(), null, new PipDetails("Missed dates", start, start.AddDays(60), [new PipObjective("Deliver", "90%", null)], null));
        Should.Throw<DomainRuleException>(() => plan.AddNote(start, null, "Too early"));
        plan.Start();
        plan.AddNote(start.AddDays(14), null, "On track");
        plan.Extend(start.AddDays(90));
        Should.Throw<DomainRuleException>(() => plan.Extend(start.AddDays(30)));
        plan.Close(false, "Did not improve", null, start.AddDays(90));
        plan.Status.ShouldBe(PipStatus.Failed);
        plan.Notes.Count.ShouldBe(1);
    }
}

public sealed class LearningTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void Completing_a_course_with_a_validity_issues_a_certificate_that_expires()
    {
        var enrollment = Enrollment.Enroll(Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Assigned, null, Today.AddDays(30), null);
        enrollment.UpdateProgress(40, Today);
        enrollment.Status.ShouldBe(EnrollmentStatus.InProgress);
        Should.Throw<DomainRuleException>(() => enrollment.UpdateProgress(100, Today));
        enrollment.Complete(Today, 88, 12, "COC-2026-ABC");
        enrollment.CertificateExpiresOn.ShouldBe(Today.AddMonths(12));
        enrollment.Progress.ShouldBe(100);
        Should.Throw<DomainRuleException>(enrollment.Cancel);
    }

    [Fact]
    public void A_course_without_a_validity_issues_no_certificate()
    {
        var enrollment = Enrollment.Enroll(Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Self, null, null, null);
        enrollment.Complete(Today, null, null, null);
        enrollment.CertificateNumber.ShouldBeNull();
        Should.Throw<DomainRuleException>(enrollment.Expire);
    }

    [Fact]
    public void Overdue_learning_is_reminded_weekly_and_an_expiring_certificate_once()
    {
        var overdue = Enrollment.Enroll(Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Assigned, null, Today.AddDays(-1), null);
        overdue.NeedsOverdueReminder(Today).ShouldBeTrue();
        overdue.Reminded(Today);
        overdue.NeedsOverdueReminder(Today.AddDays(6)).ShouldBeFalse();
        overdue.NeedsOverdueReminder(Today.AddDays(7)).ShouldBeTrue();

        var certified = Enrollment.Enroll(Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Assigned, null, null, null);
        certified.Complete(Today.AddMonths(-12).AddDays(20), null, 12, "X-1");
        certified.NeedsExpiryReminder(Today).ShouldBeTrue();
        certified.Reminded(Today);
        certified.NeedsExpiryReminder(Today.AddDays(5)).ShouldBeFalse();
    }

    [Fact]
    public void Ratings_come_after_completion()
    {
        var enrollment = Enrollment.Enroll(Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Self, null, null, null);
        Should.Throw<DomainRuleException>(() => enrollment.Rate(5, null));
        enrollment.Complete(Today, null, null, null);
        Should.Throw<DomainRuleException>(() => enrollment.Rate(6, null));
        enrollment.Rate(4, "Useful");
        enrollment.Rating.ShouldBe(4);
    }

    [Fact]
    public void Courses_and_paths_validate_their_settings()
    {
        Should.Throw<DomainRuleException>(() => Course.Create(LearningDefaults.Courses[0] with { CertificateValidityMonths = 0 }));
        Course.Create(LearningDefaults.Courses[0] with { Code = " coc " }).Code.ShouldBe("COC");
        Should.Throw<DomainRuleException>(() => LearningPath.Create("P", "Empty", null, []));
        LearningDefaults.Courses.Count(course => course.Mandatory && course.AssignToNewJoiners).ShouldBe(3);
    }
}

public sealed class CareerTests
{
    private static readonly Guid Engineer = Guid.NewGuid();
    private static readonly Guid Senior = Guid.NewGuid();
    private static readonly Guid GradeFive = Guid.NewGuid();

    private static CareerPath Path() => CareerPath.Create("Engineering", null, CareerTrack.Individual, null,
    [
        new CareerStep("Engineer", Engineer, null, 12, ["C#"], null),
        new CareerStep("Senior", Senior, null, 24, ["Design"], null),
        new CareerStep("Staff", null, GradeFive, 36, [], null),
    ]);

    [Fact]
    public void A_person_is_placed_on_a_path_by_job_or_else_grade()
    {
        CareerPath path = Path();
        path.StepOf(Engineer, null).ShouldBe(0);
        path.StepOf(Senior, GradeFive).ShouldBe(1);
        path.StepOf(Guid.NewGuid(), GradeFive).ShouldBe(2);
        path.StepOf(Guid.NewGuid(), Guid.NewGuid()).ShouldBe(-1);
        Should.Throw<DomainRuleException>(() => CareerPath.Create("One step", null, CareerTrack.Individual, null, [new CareerStep("Only", null, null, 0, [], null)]));
    }

    [Fact]
    public void A_promotion_does_not_cut_pay_and_reports_its_increase()
    {
        var details = new PromotionDetails("Senior Engineer", Senior, null, null, 1_200_000, new DateOnly(2026, 11, 1), "Leads billing");
        var nomination = PromotionNomination.Draft(Guid.NewGuid(), Guid.NewGuid(), "Engineer", 1_000_000, 4, details);
        nomination.IncreasePercent.ShouldBe(20);
        Should.Throw<DomainRuleException>(() => nomination.Update(details with { ProposedAnnualCtc = 900_000 })).ErrorCode.ShouldBe("PROMOTION_CTC");

        nomination.Submit();
        nomination.Decide(approved: true, DateTime.UtcNow);
        nomination.IsDue(new DateOnly(2026, 10, 31)).ShouldBeFalse();
        nomination.IsDue(new DateOnly(2026, 11, 1)).ShouldBeTrue();
        nomination.MarkApplied(new DateOnly(2026, 11, 1));
        Should.Throw<DomainRuleException>(nomination.Withdraw);
    }

    [Fact]
    public void A_succession_plan_is_covered_by_a_successor_ready_now_who_is_not_the_incumbent()
    {
        Guid incumbent = Guid.NewGuid();
        Guid successor = Guid.NewGuid();
        var plan = SuccessionPlan.Create("Head of delivery", null, null, incumbent, Criticality.Critical, Criticality.High, null,
            [new SuccessorEntry(successor, Readiness.OneToTwoYears, null), new SuccessorEntry(successor, Readiness.ReadyNow, "dup")]);
        plan.Successors.Count.ShouldBe(1);
        plan.Covered.ShouldBeFalse();
        plan.Update("Head of delivery", null, null, incumbent, Criticality.Critical, Criticality.High, null, [new SuccessorEntry(successor, Readiness.ReadyNow, null)]);
        plan.Covered.ShouldBeTrue();
        Should.Throw<DomainRuleException>(() => plan.Update("X", null, null, incumbent, Criticality.Low, Criticality.Low, null, [new SuccessorEntry(incumbent, Readiness.ReadyNow, null)]));
    }

    [Theory]
    [InlineData(1, Level.Low, 1)]
    [InlineData(3, Level.Medium, 5)]
    [InlineData(5, Level.Low, 3)]
    [InlineData(2, Level.High, 7)]
    [InlineData(4, Level.High, 9)]
    public void The_9_box_crosses_performance_with_potential(int rating, Level potential, int box)
        => NineBox.Box(NineBox.PerformanceBand(rating), potential).ShouldBe(box);
}
