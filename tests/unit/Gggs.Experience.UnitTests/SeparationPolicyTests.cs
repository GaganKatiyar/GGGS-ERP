using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Separation v2 phase A: notice rules, early release and buyout, exit classification.</summary>
public sealed class SeparationPolicyTests
{
    private static readonly Guid Senior = Guid.NewGuid();

    private static SeparationCase Resignation(int noticeDays = 60)
        => SeparationCase.Open(Guid.NewGuid(), SeparationType.Resignation, "Moving on", new DateOnly(2026, 10, 1), noticeDays, new DateOnly(2026, 10, 1).AddDays(noticeDays));

    [Fact]
    public void The_most_specific_notice_rule_wins()
    {
        NoticeRule[] rules =
        [
            NoticeRule.Create(null, "FULL_TIME", null, 60),
            NoticeRule.Create(Senior, null, null, 90),
            NoticeRule.Create(Senior, "FULL_TIME", null, 120),
            NoticeRule.Create(null, null, true, 15),
        ];

        NoticeRule Pick(Guid? grade, string type, bool onProbation)
            => rules.Where(rule => rule.Matches(grade, type, onProbation)).OrderByDescending(rule => rule.Specificity).First();

        Pick(Senior, "FULL_TIME", false).NoticeDays.ShouldBe(120);
        Pick(Senior, "CONTRACT", false).NoticeDays.ShouldBe(90);
        Pick(null, "FULL_TIME", false).NoticeDays.ShouldBe(60);
        Pick(Senior, "FULL_TIME", true).NoticeDays.ShouldBe(15, "a probation rule overrides grade and type");
    }

    [Fact]
    public void A_notice_rule_must_say_who_it_is_for()
        => Should.Throw<DomainRuleException>(() => NoticeRule.Create(null, " ", null, 30)).ErrorCode.ShouldBe("NOTICE_RULE_EMPTY");

    [Fact]
    public void Policy_defaults_follow_indian_law_and_reject_nonsense()
    {
        SeparationPolicy policy = SeparationPolicy.CreateDefault();
        policy.SettlementDueWorkingDays.ShouldBe(2, "Code on Wages s.17(2)");
        policy.GratuityDueDays.ShouldBe(30, "Payment of Gratuity Act s.7");
        policy.BlockedLeaveTypes.ShouldBe(["EL"]);

        Should.Throw<DomainRuleException>(() => policy.Update(60, 15, true, false, [], ["el", "EL"], null, 40, 7, 2, 30));
        policy.Update(45, 7, false, true, ["el"], ["el", "EL"], 30, 58, 5, 3, 30);
        policy.EncashableLeaveTypes.ShouldBe(["EL"]);
        policy.RetirementAge.ShouldBe(58);
    }

    [Fact]
    public void An_early_release_with_buyout_keeps_the_shortfall_and_without_it_waives_it()
    {
        SeparationCase bought = Resignation();
        bought.Approve(null);
        bought.RequestEarlyRelease(new DateOnly(2026, 10, 21), withBuyout: true);
        bought.DecideEarlyRelease(granted: true);
        bought.LastWorkingDay.ShouldBe(new DateOnly(2026, 10, 21));
        bought.ShortfallDays.ShouldBe(40);
        bought.ShortfallWaived.ShouldBeFalse();
        bought.RequestedReleaseDay.ShouldBeNull();

        SeparationCase released = Resignation();
        released.Approve(null);
        released.RequestEarlyRelease(new DateOnly(2026, 10, 21), withBuyout: false);
        released.DecideEarlyRelease(granted: true);
        released.ShortfallWaived.ShouldBeTrue();
    }

    [Fact]
    public void A_declined_or_impossible_early_release_leaves_the_last_day_alone()
    {
        SeparationCase separation = Resignation();
        separation.Approve(null);
        Should.Throw<DomainRuleException>(() => separation.RequestEarlyRelease(new DateOnly(2026, 12, 31), withBuyout: true)).ErrorCode.ShouldBe("SEPARATION_RELEASE_DAY");
        separation.RequestEarlyRelease(new DateOnly(2026, 11, 1), withBuyout: true);
        separation.DecideEarlyRelease(granted: false);
        separation.LastWorkingDay.ShouldBe(new DateOnly(2026, 11, 30));
        Should.Throw<DomainRuleException>(() => separation.DecideEarlyRelease(granted: true)).ErrorCode.ShouldBe("SEPARATION_RELEASE_NONE");
    }

    [Fact]
    public void Exits_are_classified_with_known_reasons_only()
    {
        SeparationCase separation = Resignation();
        separation.Classify("COMPENSATION", "COMPENSATION", regretted: true, " Offered 10% - declined ");
        separation.ReasonCode.ShouldBe("COMPENSATION");
        separation.SecondaryReasonCode.ShouldBeNull("the same reason twice is one reason");
        separation.RetentionNote.ShouldBe("Offered 10% - declined");
        Should.Throw<DomainRuleException>(() => separation.Classify("BORED", null, null, null)).ErrorCode.ShouldBe("SEPARATION_REASON");

        SeparationCase.Open(Guid.NewGuid(), SeparationType.Retirement, "Superannuation", new DateOnly(2026, 10, 1), 0, new DateOnly(2026, 10, 31)).ReasonCode.ShouldBe("RETIREMENT");
    }
}
