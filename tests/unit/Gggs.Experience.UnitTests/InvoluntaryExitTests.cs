using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Separation v2 phase C: show-cause and termination, absconding, retrenchment, death, proposals.</summary>
public sealed class InvoluntaryExitTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static ShowCause Notice() => ShowCause.Issue(Guid.NewGuid(), "Repeatedly missed client deliveries without explanation.", Today, 7, null);

    [Fact]
    public void Nobody_is_terminated_before_they_have_been_heard()
    {
        ShowCause notice = Notice();
        notice.ReplyDueOn.ShouldBe(Today.AddDays(7));
        notice.Heard(Today.AddDays(3)).ShouldBeFalse();
        Should.Throw<DomainRuleException>(() => notice.Decide(ShowCauseOutcome.Termination, null, Today.AddDays(3))).ErrorCode.ShouldBe("SHOW_CAUSE_NOT_HEARD");

        notice.Heard(Today.AddDays(8)).ShouldBeTrue("the time to reply has passed");
        notice.Decide(ShowCauseOutcome.Termination, "No reply received", Today.AddDays(8));
        notice.Status.ShouldBe(ShowCauseStatus.Closed);
        Should.Throw<DomainRuleException>(() => notice.RecordReply("Late reply", Today.AddDays(9))).ErrorCode.ShouldBe("SHOW_CAUSE_STATE");
    }

    [Fact]
    public void A_reply_lets_hr_decide_at_once_and_a_warning_needs_no_hearing()
    {
        ShowCause replied = Notice();
        replied.RecordReply("  The client changed the scope twice.  ", Today.AddDays(2));
        replied.Reply.ShouldBe("The client changed the scope twice.");
        replied.Status.ShouldBe(ShowCauseStatus.Replied);
        replied.Heard(Today.AddDays(2)).ShouldBeTrue();

        ShowCause warned = Notice();
        warned.Decide(ShowCauseOutcome.Warning, "First lapse", Today);
        warned.Outcome.ShouldBe(ShowCauseOutcome.Warning);
        Should.Throw<DomainRuleException>(() => Notice().Decide(ShowCauseOutcome.None, null, Today)).ErrorCode.ShouldBe("SHOW_CAUSE_OUTCOME");
    }

    [Fact]
    public void An_open_enquiry_must_report_before_a_termination()
    {
        ShowCause notice = Notice();
        Should.Throw<DomainRuleException>(() => notice.StartEnquiry(notice.EmployeeId)).ErrorCode.ShouldBe("SHOW_CAUSE_OFFICER");
        Should.Throw<DomainRuleException>(() => notice.RecordFindings("Too early", Today)).ErrorCode.ShouldBe("SHOW_CAUSE_STATE");

        notice.RecordReply("I deny it.", Today.AddDays(1));
        notice.StartEnquiry(Guid.NewGuid());
        Should.Throw<DomainRuleException>(() => notice.Decide(ShowCauseOutcome.Termination, null, Today.AddDays(5))).ErrorCode.ShouldBe("SHOW_CAUSE_ENQUIRY_OPEN");
        notice.RecordFindings("Allegation proved on the delivery logs.", Today.AddDays(10));
        notice.Decide(ShowCauseOutcome.Termination, null, Today.AddDays(11));
        notice.Outcome.ShouldBe(ShowCauseOutcome.Termination);
    }

    [Fact]
    public void Absconding_needs_a_lapsed_notice_and_stops_at_the_final_one()
    {
        AbsenceAlert alert = AbsenceAlert.Raise(Guid.NewGuid(), Today.AddDays(-10), Today.AddDays(-11), 8, Today);
        Should.Throw<DomainRuleException>(() => alert.Separated(Guid.NewGuid(), Today)).ErrorCode.ShouldBe("ABSENCE_NOT_LAPSED");

        alert.SendNotice(Today, 7, Guid.NewGuid());
        alert.NoticeLapsed(Today.AddDays(7)).ShouldBeFalse("the respond-by day itself still counts");
        alert.NoticeLapsed(Today.AddDays(8)).ShouldBeTrue();
        alert.SendNotice(Today.AddDays(8), 7, Guid.NewGuid());
        alert.SendNotice(Today.AddDays(16), 7, Guid.NewGuid());
        Should.Throw<DomainRuleException>(() => alert.SendNotice(Today.AddDays(24), 7, Guid.NewGuid())).ErrorCode.ShouldBe("ABSENCE_NOTICES");
        AbsenceAlert.NoticeTitle(3).ShouldBe("Final notice");

        alert.Extend(5);
        alert.WorkingDaysAbsent.ShouldBe(8, "a run never shrinks");
        alert.Separated(Guid.NewGuid(), Today.AddDays(24));
        alert.Status.ShouldBe(AbsenceAlertStatus.Separated);
        Should.Throw<DomainRuleException>(() => alert.Close(AbsenceAlertStatus.Returned, null)).ErrorCode.ShouldBe("ABSENCE_STATE");
    }

    [Theory]
    [InlineData("2025-11-01", false, 0, 0)]
    [InlineData("2023-04-21", true, 3, 51_923)]
    [InlineData("2023-03-01", true, 4, 69_231)]
    public void Retrenchment_compensation_is_fifteen_days_basic_per_year_or_part_over_six_months(string joined, bool eligible, int years, int amount)
    {
        (int counted, decimal compensation, bool qualifies) = SettlementMath.Retrenchment(30_000m, DateOnly.Parse(joined, System.Globalization.CultureInfo.InvariantCulture), Today);
        qualifies.ShouldBe(eligible);
        counted.ShouldBe(years);
        compensation.ShouldBe(amount);
    }

    [Fact]
    public void Notice_pay_is_for_exits_the_company_starts()
    {
        SeparationCase retrenched = SeparationCase.Open(Guid.NewGuid(), SeparationType.Retrenchment, "Unit closed", Today, 10, Today.AddDays(10));
        retrenched.ReasonCode.ShouldBe("RESTRUCTURING");
        retrenched.ExitReasonCode.ShouldBe("RETRENCHMENT");
        retrenched.SetPayInLieu(20);
        retrenched.PayInLieuDays.ShouldBe(20);
        SettlementMath.NoticePay(45_000m, 20).ShouldBe(30_000m);

        SeparationCase resigned = SeparationCase.Open(Guid.NewGuid(), SeparationType.Resignation, "Moving on", Today, 60, Today.AddDays(60));
        Should.Throw<DomainRuleException>(() => resigned.SetPayInLieu(10)).ErrorCode.ShouldBe("SEPARATION_PAY_IN_LIEU");
        Should.Throw<DomainRuleException>(() => resigned.SetNominee("Asha", "Spouse", null)).ErrorCode.ShouldBe("SEPARATION_NOMINEE");
    }

    [Fact]
    public void A_death_records_the_nominee_and_proposals_wait_for_confirmation()
    {
        SeparationCase death = SeparationCase.Open(Guid.NewGuid(), SeparationType.Death, "Passed away", Today, 0, Today);
        death.SetNominee(" Asha Rao ", "Spouse", "asha@example.com");
        death.NomineeName.ShouldBe("Asha Rao");
        Should.Throw<DomainRuleException>(() => death.Propose()).ErrorCode.ShouldBe("SEPARATION_PROPOSE");

        SeparationCase retirement = SeparationCase.Open(Guid.NewGuid(), SeparationType.Retirement, "Superannuation at 60", Today, 40, Today.AddDays(40));
        retirement.AwaitingConfirmation.ShouldBeFalse();
        retirement.Propose();
        retirement.AwaitingConfirmation.ShouldBeTrue();
        retirement.Approve(null);
        retirement.AwaitingConfirmation.ShouldBeFalse();
    }

    [Fact]
    public void The_policy_bounds_reply_time_and_proposal_lead()
    {
        SeparationPolicy policy = SeparationPolicy.CreateDefault();
        policy.ShowCauseReplyDays.ShouldBe(7);
        policy.ExitProposalLeadDays.ShouldBe(60);
        Should.Throw<DomainRuleException>(() => policy.Update(60, 15, true, false, ["EL"], ["EL"], null, 60, 7, 2, 30, showCauseReplyDays: 0));
        Should.Throw<DomainRuleException>(() => policy.Update(60, 15, true, false, ["EL"], ["EL"], null, 60, 7, 2, 30, exitProposalLeadDays: 400));
        policy.Update(60, 15, true, false, ["EL"], ["EL"], null, 58, 7, 2, 30, 10, 90);
        policy.ExitProposalLeadDays.ShouldBe(90);
        policy.FillMissingSettings().ShouldBeFalse("nothing is missing");
    }
}
