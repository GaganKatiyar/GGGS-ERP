using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Onboarding.Domain;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Workforce.Domain;
using Shouldly;

namespace Gggs.Hiring.UnitTests;

public sealed class CandidateMatcherTests
{
    private static readonly string[] Required = ["C#", "SQL", "Azure"];

    [Fact]
    public void A_candidate_with_every_skill_in_range_and_within_budget_scores_full_marks()
    {
        MatchBreakdown match = CandidateMatcher.Match(Required, 3, 8, 1_500_000, ["c#", "sql", "AZURE", "Go"], 5, 1_400_000);

        match.Score.ShouldBe(100);
        match.MissingSkills.ShouldBeEmpty();
        match.MatchedSkills.Count.ShouldBe(3);
    }

    [Fact]
    public void Missing_skills_cost_their_share_of_the_sixty_skill_points()
    {
        MatchBreakdown match = CandidateMatcher.Match(Required, null, null, null, ["C#"], null, null);

        match.SkillsScore.ShouldBe(20);
        match.MissingSkills.ShouldBe(["SQL", "Azure"]);
        match.Score.ShouldBe(20 + CandidateMatcher.ExperienceWeight + CandidateMatcher.SalaryWeight);
    }

    [Theory]
    [InlineData(5, 25)]
    [InlineData(2, 20)]
    [InlineData(10, 15)]
    [InlineData(0, 10)]
    [InlineData(20, 0)]
    public void Experience_outside_the_range_loses_a_fifth_of_the_points_per_year(double years, int expected)
        => CandidateMatcher.Match([], 3, 8, null, [], (decimal)years, null).ExperienceScore.ShouldBe(expected);

    [Fact]
    public void Unknown_experience_scores_nothing_when_a_range_is_asked_for()
        => CandidateMatcher.Match([], 3, null, null, [], null, null).ExperienceScore.ShouldBe(0);

    [Theory]
    [InlineData(1_000_000, 15)]
    [InlineData(1_100_000, 10)]
    [InlineData(1_200_000, 5)]
    [InlineData(1_300_000, 0)]
    public void Asking_above_the_budget_costs_a_third_of_the_salary_points_per_ten_percent(int expected, int score)
        => CandidateMatcher.Match([], null, null, 1_000_000, [], null, expected).SalaryScore.ShouldBe(score);

    [Fact]
    public void A_requisition_without_criteria_rates_everyone_fully()
        => CandidateMatcher.Match([], null, null, null, [], null, null).Score.ShouldBe(100);
}

public sealed class RecruitmentDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static RequisitionDetails Details(int openings = 2) => new(
        "Engineer", Guid.NewGuid(), null, null, null, openings, "full_time", "hybrid", 2, 6, 800_000, 1_200_000, ["C#", " c# ", "SQL"], null, null, null, "High", null);

    private static CandidateDetails Person(string email = "Asha@Example.com", string? mobile = "+91 98450-12345") => new(
        "Asha", "Rao", email, mobile, null, null, 4, 900_000, 1_100_000, 30, "Pune", ["C#"], null, null);

    [Fact]
    public void A_requisition_normalises_codes_and_skills_and_fills_on_its_last_hire()
    {
        var requisition = Requisition.Open("REQ-2026-0001", null, Details(openings: 2), Today);

        requisition.EmploymentTypeCode.ShouldBe("FULL_TIME");
        requisition.Skills.ShouldBe(["C#", "SQL"]);
        requisition.RecordHire(Today).ShouldBeFalse();
        requisition.Remaining.ShouldBe(1);
        requisition.RecordHire(Today).ShouldBeTrue();
        requisition.Status.ShouldBe(RequisitionStatus.Filled);
        Should.Throw<DomainRuleException>(() => requisition.RecordHire(Today));
        Should.Throw<DomainRuleException>(() => requisition.Update(Details()));
    }

    [Fact]
    public void A_requisition_on_hold_takes_no_applications_and_cannot_shrink_below_its_hires()
    {
        var requisition = Requisition.Open("REQ-2026-0002", null, Details(openings: 3), Today);
        requisition.RecordHire(Today);
        requisition.RecordHire(Today);

        Should.Throw<DomainRuleException>(() => requisition.Update(Details(openings: 1))).ErrorCode.ShouldBe("REQUISITION_OPENINGS");
        requisition.Hold();
        Should.Throw<DomainRuleException>(requisition.EnsureAccepting);
        requisition.Resume();
        requisition.EnsureAccepting();
    }

    [Fact]
    public void A_candidate_keeps_a_normalised_email_and_mobile_for_deduplication()
    {
        var candidate = Candidate.Create("CAN-000001", Person(), CandidateSource.Direct, null, null);

        candidate.Email.ShouldBe("asha@example.com");
        candidate.Mobile.ShouldBe("919845012345");
        candidate.FullName.ShouldBe("Asha Rao");
        Should.Throw<DomainRuleException>(() => Candidate.Create("CAN-2", Person(email: "not-an-email"), CandidateSource.Direct, null, null));
        Should.Throw<DomainRuleException>(() => Candidate.Create("CAN-3", Person(mobile: "12"), CandidateSource.Direct, null, null));
        Should.Throw<DomainRuleException>(() => Candidate.Create("CAN-4", Person(), CandidateSource.Agency, null, null)).ErrorCode.ShouldBe("CANDIDATE_AGENCY");
        Should.Throw<DomainRuleException>(() => Candidate.Create("CAN-5", Person(), CandidateSource.Referral, null, null)).ErrorCode.ShouldBe("CANDIDATE_REFERRER");
    }

    [Fact]
    public void An_application_records_each_move_and_closes_on_rejection()
    {
        var application = JobApplication.Apply(Guid.NewGuid(), Guid.NewGuid(), 140, null, Now);
        application.MatchScore.ShouldBe(100);

        application.MoveTo(ApplicationStage.Screening, null, Now.AddHours(1), "Phone screen");
        application.MoveTo(ApplicationStage.Interview, null, Now.AddHours(2), null);
        Should.Throw<DomainRuleException>(() => application.MoveTo(ApplicationStage.Hired, null, Now, null));
        Should.Throw<DomainRuleException>(() => application.Hire(null, Now));
        application.Reject("Not enough depth", null, Now.AddHours(3));

        application.History.Select(change => change.Stage).ShouldBe([ApplicationStage.Applied, ApplicationStage.Screening, ApplicationStage.Interview, ApplicationStage.Rejected]);
        application.History[1].Note.ShouldBe("Phone screen");
        application.IsClosed.ShouldBeTrue();
        Should.Throw<DomainRuleException>(() => application.MoveTo(ApplicationStage.Screening, null, Now, null)).ErrorCode.ShouldBe("APPLICATION_CLOSED");
    }

    [Fact]
    public void An_offer_goes_through_approval_and_sending_before_it_can_be_accepted()
    {
        var offer = Offer.Draft("OFF-2026-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new OfferTerms(1_200_000, "inr", Today.AddDays(30), "Engineer", null, Today.AddDays(7), null));
        offer.Currency.ShouldBe("INR");

        Should.Throw<DomainRuleException>(() => offer.Send(Now));
        offer.Submit();
        offer.Decide(approved: null);
        offer.Status.ShouldBe(OfferStatus.Draft);
        offer.Submit();
        offer.Decide(approved: true);
        offer.Send(Now);
        Should.Throw<DomainRuleException>(() => offer.Respond(accepted: true, null, Now, Today.AddDays(8))).ErrorCode.ShouldBe("OFFER_EXPIRED");
        offer.Respond(accepted: true, null, Now, Today.AddDays(2));
        offer.Status.ShouldBe(OfferStatus.Accepted);
        Should.Throw<DomainRuleException>(offer.Withdraw);
    }

    [Fact]
    public void An_offer_must_expire_before_joining_and_pay_something()
    {
        Should.Throw<DomainRuleException>(() => Offer.Draft("O", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new OfferTerms(0, "INR", Today.AddDays(30), "X", null, null, null))).ErrorCode.ShouldBe("OFFER_CTC");
        Should.Throw<DomainRuleException>(() => Offer.Draft("O", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new OfferTerms(1, "INR", Today, "X", null, Today.AddDays(1), null))).ErrorCode.ShouldBe("OFFER_EXPIRY");
    }

    [Fact]
    public void A_scorecard_averages_its_ratings_and_rejects_out_of_range_ones()
    {
        var scorecard = Scorecard.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, int> { ["Technical"] = 5, ["Communication"] = 4, ["Fit"] = 4 }, Recommendation.Yes, null, Now);
        scorecard.OverallRating.ShouldBe(4.33m);

        Should.Throw<DomainRuleException>(() => scorecard.Revise(new Dictionary<string, int> { ["Technical"] = 0 }, Recommendation.No, null, Now));
        Should.Throw<DomainRuleException>(() => scorecard.Revise(new Dictionary<string, int>(), Recommendation.No, null, Now));
    }

    [Fact]
    public void An_interview_needs_a_panel_and_a_sensible_length()
    {
        Should.Throw<DomainRuleException>(() => Interview.Schedule(Guid.NewGuid(), "Tech", Now, 60, InterviewMode.Video, null, [], null)).ErrorCode.ShouldBe("INTERVIEW_PANEL");
        Should.Throw<DomainRuleException>(() => Interview.Schedule(Guid.NewGuid(), "Tech", Now, 5, InterviewMode.Video, null, [Guid.NewGuid()], null)).ErrorCode.ShouldBe("INTERVIEW_DURATION");

        Guid member = Guid.NewGuid();
        var interview = Interview.Schedule(Guid.NewGuid(), "Tech", Now, 60, InterviewMode.Video, null, [member, member], null);
        interview.Panel.ShouldBe([member]);
        interview.Cancel(noShow: true);
        interview.Status.ShouldBe(InterviewStatus.NoShow);
        Should.Throw<DomainRuleException>(() => interview.Cancel(noShow: false));
    }

    [Fact]
    public void A_failed_background_check_needs_remarks_and_a_completed_one_cannot_change()
    {
        var check = BackgroundCheck.Initiate(Guid.NewGuid(), null, "VerifyCo", ["Identity", "identity", "Education"], Today);
        check.Checks.Count.ShouldBe(2);

        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Failed, null, Today)).ErrorCode.ShouldBe("BGV_REMARKS");
        check.Record(BackgroundCheckStatus.InProgress, null, Today);
        check.CompletedOn.ShouldBeNull();
        check.Record(BackgroundCheckStatus.Clear, null, Today);
        check.CompletedOn.ShouldBe(Today);
        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Failed, "late", Today));
    }

    [Fact]
    public void A_referral_carries_its_bonus_only_when_there_is_one()
    {
        var referral = Referral.Submit(Guid.NewGuid(), Guid.NewGuid(), null, null);
        referral.InProcess();
        referral.Status.ShouldBe(ReferralStatus.InProcess);

        referral.Hired(Guid.NewGuid(), 0, Today);
        referral.BonusAmount.ShouldBeNull();
        referral.BonusDueOn.ShouldBeNull();
        referral.NotSelected();
        referral.Status.ShouldBe(ReferralStatus.Hired);
    }

    [Fact]
    public void Recruitment_rules_need_criteria_and_sensible_limits()
    {
        RecruitmentSettings settings = RecruitmentSettings.Defaults();
        settings.ToRules().ScorecardCriteria.ShouldBe(RecruitmentRules.Default.ScorecardCriteria);

        Should.Throw<DomainRuleException>(() => settings.Update(RecruitmentRules.Default with { ScorecardCriteria = [] }));
        Should.Throw<DomainRuleException>(() => settings.Update(RecruitmentRules.Default with { OfferValidDays = 0 }));
        settings.Update(RecruitmentRules.Default with { Currency = "usd", ReferralBonus = 500 });
        settings.ToRules().Currency.ShouldBe("USD");
    }
}

public sealed class WorkforceDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static RequestDetails Request(int positions = 2, RequestReason reason = RequestReason.Expansion, Guid? replacing = null) => new(
        "Analyst", Guid.NewGuid(), null, null, null, positions, ["Excel", "excel", " SQL "], 1, 3, "full_time", "office", null, null, null, RequestPriority.Medium, null, reason, replacing, null);

    [Fact]
    public void A_plan_is_editable_until_submitted_and_a_send_back_returns_it_to_draft()
    {
        var plan = WorkforcePlan.Draft("FY27", 2027, new DateOnly(2027, 4, 1), null, null);
        plan.Submit();
        Should.Throw<DomainRuleException>(plan.EnsureEditable);

        plan.Decide(approved: null, Now);
        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.Submit();
        plan.Decide(approved: true, Now);
        plan.Status.ShouldBe(PlanStatus.Approved);
        plan.Close();
        Should.Throw<DomainRuleException>(() => WorkforcePlan.Draft("Bad", 1999, new DateOnly(1999, 1, 1), null, null)).ErrorCode.ShouldBe("PLAN_YEAR");
    }

    [Fact]
    public void A_plan_line_reports_its_gap()
    {
        var line = WorkforcePlanLine.For(Guid.NewGuid(), new PlanLineDetails(Guid.NewGuid(), null, null, 12, 9_000_000.456m, 500_000, null), 9);
        line.HeadcountGap.ShouldBe(3);
        line.SalaryBudget.ShouldBe(9_000_000.46m);
        line.Refresh(14);
        line.HeadcountGap.ShouldBe(-2);
        Should.Throw<DomainRuleException>(() => line.Update(new PlanLineDetails(Guid.NewGuid(), null, null, -1, 0, 0, null), 0));
    }

    [Fact]
    public void A_request_dedupes_its_skills_and_a_replacement_names_who_left()
    {
        var request = ManpowerRequest.Raise("MPR-2026-0001", Guid.NewGuid(), Request());
        request.Skills.ShouldBe(["Excel", "SQL"]);
        request.EmploymentTypeCode.ShouldBe("FULL_TIME");

        Should.Throw<DomainRuleException>(() => ManpowerRequest.Raise("MPR-2", Guid.NewGuid(), Request(reason: RequestReason.Replacement))).ErrorCode.ShouldBe("REQUEST_REPLACEMENT");
        Should.Throw<DomainRuleException>(() => ManpowerRequest.Raise("MPR-3", Guid.NewGuid(), Request(positions: 0))).ErrorCode.ShouldBe("REQUEST_POSITIONS");
    }

    [Fact]
    public void An_approved_request_is_fulfilled_once_and_cannot_be_edited()
    {
        var request = ManpowerRequest.Raise("MPR-2026-0002", Guid.NewGuid(), Request());
        request.Submit();
        request.Decide(approved: true, Now);

        Should.Throw<DomainRuleException>(() => request.Update(Request()));
        request.Fulfil();
        request.Status.ShouldBe(ManpowerRequestStatus.Fulfilled);
        Should.Throw<DomainRuleException>(request.Cancel);
    }
}

public sealed class OnboardingDomainTests
{
    private static readonly DateOnly Joining = new(2026, 11, 2);

    private static TemplateDetails Template(Guid? department = null, string? type = null, bool isDefault = false) => new(
        "Checklist", null, department, null, type, isDefault, [new TemplateTask("Laptop", null, TaskCategory.Laptop, AssigneeRole.It, -1), new TemplateTask("Induction", null, TaskCategory.Induction, AssigneeRole.Hr, 0)]);

    [Fact]
    public void The_most_specific_applicable_template_wins_and_the_default_fits_everyone()
    {
        Guid engineering = Guid.NewGuid();
        var fallback = OnboardingTemplate.Create(Template(isDefault: true));
        var departmental = OnboardingTemplate.Create(Template(department: engineering));
        var contractors = OnboardingTemplate.Create(Template(department: engineering, type: "contract"));

        fallback.Fit(engineering, null, "FULL_TIME").ShouldBe(0);
        departmental.Fit(engineering, null, "FULL_TIME").ShouldBe(1);
        contractors.Fit(engineering, null, "FULL_TIME").ShouldBe(-1);
        contractors.Fit(engineering, null, "Contract").ShouldBe(2);
        departmental.Fit(Guid.NewGuid(), null, null).ShouldBe(-1);
    }

    [Fact]
    public void A_default_template_cannot_be_narrowed_and_tasks_need_valid_offsets()
    {
        Should.Throw<DomainRuleException>(() => OnboardingTemplate.Create(Template(department: Guid.NewGuid(), isDefault: true))).ErrorCode.ShouldBe("TEMPLATE_DEFAULT");
        Should.Throw<DomainRuleException>(() => OnboardingTemplate.Create(new TemplateDetails("X", null, null, null, null, false, [new TemplateTask("Late", null, TaskCategory.Other, AssigneeRole.Hr, 400)])));
        Should.Throw<DomainRuleException>(() => OnboardingTemplate.Create(new TemplateDetails("X", null, null, null, null, false, [])));
        OnboardingTemplate.Create(Template()).Tasks[0].Title.ShouldBe("Laptop");
    }

    [Fact]
    public void A_case_pre_boards_until_joining_then_runs_until_complete()
    {
        var template = OnboardingTemplate.Create(Template(isDefault: true));
        var onboarding = OnboardingCase.Start(Guid.NewGuid(), template, Joining, null, null, Joining.AddDays(-5));
        onboarding.Status.ShouldBe(CaseStatus.PreBoarding);

        onboarding.Advance(Joining.AddDays(-1)).ShouldBeFalse();
        onboarding.Advance(Joining).ShouldBeTrue();
        onboarding.Complete(Joining.AddDays(10));
        onboarding.Reopen(Joining.AddDays(11));
        onboarding.Status.ShouldBe(CaseStatus.InProgress);
        onboarding.Cancel();
        Should.Throw<DomainRuleException>(() => onboarding.AssignBuddy(null)).ErrorCode.ShouldBe("ONBOARDING_CLOSED");
    }

    [Fact]
    public void A_task_falls_due_relative_to_joining_and_is_reminded_every_three_days_while_overdue()
    {
        var task = OnboardingTask.From(Guid.NewGuid(), new TemplateTask("Laptop", null, TaskCategory.Laptop, AssigneeRole.It, -1), Joining, Guid.NewGuid(), 0);
        task.DueDate.ShouldBe(Joining.AddDays(-1));

        task.NeedsReminder(Joining.AddDays(-1)).ShouldBeFalse();
        task.NeedsReminder(Joining).ShouldBeTrue();
        task.Reminded(Joining);
        task.NeedsReminder(Joining.AddDays(2)).ShouldBeFalse();
        task.NeedsReminder(Joining.AddDays(3)).ShouldBeTrue();

        Should.Throw<DomainRuleException>(() => task.Finish(skipped: true, null, null, DateTime.UtcNow)).ErrorCode.ShouldBe("ONBOARDING_TASK_REQUIRED");
        task.Finish(skipped: false, null, "Issued", DateTime.UtcNow);
        task.NeedsReminder(Joining.AddDays(10)).ShouldBeFalse();
    }

    [Fact]
    public void An_unassigned_task_is_never_reminded_and_an_optional_one_can_be_skipped()
    {
        var task = OnboardingTask.From(Guid.NewGuid(), new TemplateTask("Buddy", null, TaskCategory.ManagerIntroduction, AssigneeRole.Buddy, 1, Required: false), Joining, null, 0);
        task.NeedsReminder(Joining.AddDays(30)).ShouldBeFalse();
        task.Finish(skipped: true, null, null, DateTime.UtcNow);
        task.Status.ShouldBe(OnboardingTaskStatus.Skipped);
    }

    [Fact]
    public void The_default_checklist_covers_the_spec_tasks()
    {
        TaskCategory[] categories = [.. OnboardingDefaults.Tasks.Select(task => task.Category).Distinct()];
        categories.ShouldContain(TaskCategory.Documents);
        categories.ShouldContain(TaskCategory.Verification);
        categories.ShouldContain(TaskCategory.Email);
        categories.ShouldContain(TaskCategory.Laptop);
        categories.ShouldContain(TaskCategory.Vpn);
        categories.ShouldContain(TaskCategory.IdCard);
        categories.ShouldContain(TaskCategory.Induction);
        categories.ShouldContain(TaskCategory.Training);
        OnboardingDefaults.Tasks.ShouldContain(task => task.DueOffsetDays < 0);
    }
}
