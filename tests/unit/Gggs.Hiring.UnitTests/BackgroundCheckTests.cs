using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Recruitment.Domain;
using Shouldly;

namespace Gggs.Hiring.UnitTests;

/// <summary>
/// Background verification: each check has its own result, the overall status follows them, reports are kept
/// with it, and the hiring rule decides whether an unfinished verification stops a hire.
/// </summary>
public sealed class BackgroundCheckTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    private static BackgroundCheck Started() => BackgroundCheck.Initiate(Guid.NewGuid(), null, "AuthBridge", ["Identity", "Education", "Employment"], Today.AddDays(-3));

    [Fact]
    public void Each_check_starts_not_started()
    {
        BackgroundCheck check = Started();

        check.Status.ShouldBe(BackgroundCheckStatus.Initiated);
        check.Items.Select(item => item.Status).ShouldAllBe(status => status == BackgroundCheckStatus.Initiated);
    }

    [Fact]
    public void One_check_done_makes_the_verification_in_progress()
    {
        BackgroundCheck check = Started();

        check.Record(BackgroundCheckStatus.Clear, null, Today, "identity");

        check.Status.ShouldBe(BackgroundCheckStatus.InProgress);
        check.Items.Single(item => item.Name == "Identity").DoneOn.ShouldBe(Today);
        check.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void All_checks_clear_makes_it_clear()
    {
        BackgroundCheck check = Started();

        foreach (string name in new[] { "Identity", "Education", "Employment" })
        {
            check.Record(BackgroundCheckStatus.Clear, null, Today, name);
        }

        check.Status.ShouldBe(BackgroundCheckStatus.Clear);
        check.CompletedOn.ShouldBe(Today);
    }

    [Fact]
    public void A_discrepancy_shows_once_every_check_is_done_with_what_was_found()
    {
        BackgroundCheck check = Started();
        check.Record(BackgroundCheckStatus.Clear, null, Today, "Identity");
        check.Record(BackgroundCheckStatus.Discrepancy, "Joining date differs by two months", Today, "Employment");
        check.Status.ShouldBe(BackgroundCheckStatus.InProgress, "Education is still open");

        check.Record(BackgroundCheckStatus.Clear, null, Today, "Education");

        check.Status.ShouldBe(BackgroundCheckStatus.Discrepancy);
        check.Remarks.ShouldBe("Employment: Joining date differs by two months");
    }

    [Fact]
    public void A_failed_check_fails_the_verification_at_once()
    {
        BackgroundCheck check = Started();

        check.Record(BackgroundCheckStatus.Failed, "Degree certificate not issued by the university", Today, "Education");

        check.Status.ShouldBe(BackgroundCheckStatus.Failed);
        check.IsComplete.ShouldBeTrue();
        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Clear, null, Today, "Identity")).ErrorCode.ShouldBe("BGV_STATE");
    }

    [Fact]
    public void A_result_without_a_check_named_covers_every_open_check()
    {
        BackgroundCheck check = Started();
        check.Record(BackgroundCheckStatus.Discrepancy, "Address proof expired", Today, "Identity");

        check.Record(BackgroundCheckStatus.Clear, null, Today);

        check.Items.Single(item => item.Name == "Identity").Status.ShouldBe(BackgroundCheckStatus.Discrepancy, "a check already done keeps its result");
        check.Status.ShouldBe(BackgroundCheckStatus.Discrepancy);
    }

    [Fact]
    public void A_check_cannot_be_recorded_twice_or_be_one_that_was_not_ordered()
    {
        BackgroundCheck check = Started();
        check.Record(BackgroundCheckStatus.Clear, null, Today, "Identity");

        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Clear, null, Today, "Identity")).ErrorCode.ShouldBe("BGV_CHECK_DONE");
        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Clear, null, Today, "Criminal")).ErrorCode.ShouldBe("BGV_CHECK");
        Should.Throw<DomainRuleException>(() => check.Record(BackgroundCheckStatus.Failed, " ", Today, "Education")).ErrorCode.ShouldBe("BGV_REMARKS");
    }

    [Fact]
    public void Reports_are_kept_for_a_check_or_for_the_whole_verification()
    {
        BackgroundCheck check = Started();

        check.AttachReport("t-1/recruitment/bgv/a/report.pdf", "report.pdf", null, Today);
        check.AttachReport("t-1/recruitment/bgv/a/degree.jpg", "degree.jpg", "education", Today);

        check.Reports.Count.ShouldBe(2);
        check.Reports[1].CheckName.ShouldBe("Education");
        Should.Throw<DomainRuleException>(() => check.AttachReport("k", "x.pdf", "Criminal", Today)).ErrorCode.ShouldBe("BGV_CHECK");
    }

    [Theory]
    [InlineData(BgvHireRule.Off, false, null)]
    [InlineData(BgvHireRule.Warn, false, "BGV_PENDING")]
    [InlineData(BgvHireRule.Warn, true, null)]
    [InlineData(BgvHireRule.Required, true, "BGV_REQUIRED")]
    public void An_unfinished_verification_is_handled_as_the_hiring_rule_says(BgvHireRule rule, bool acknowledged, string? code)
        => BgvHireGate.Check(rule, Started(), acknowledged)?.Code.ShouldBe(code);

    [Fact]
    public void No_verification_at_all_counts_as_unfinished()
    {
        BgvHireGate.Check(BgvHireRule.Required, null, acknowledged: false)!.Value.Message.ShouldContain("has not been started");
        BgvHireGate.Check(BgvHireRule.Off, null, acknowledged: false).ShouldBeNull();
    }

    [Fact]
    public void A_failed_verification_stops_a_hire_whatever_the_rule()
    {
        BackgroundCheck check = Started();
        check.Record(BackgroundCheckStatus.Failed, "Criminal record found", Today, "Identity");

        BgvHireGate.Check(BgvHireRule.Off, check, acknowledged: true)!.Value.Code.ShouldBe("BGV_FAILED");
    }

    [Fact]
    public void A_complete_verification_with_a_discrepancy_lets_the_hire_go_ahead()
    {
        BackgroundCheck check = Started();
        check.Record(BackgroundCheckStatus.Discrepancy, "Gap year not mentioned", Today);

        BgvHireGate.Check(BgvHireRule.Required, check, acknowledged: false).ShouldBeNull();
    }
}
