using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Assets.Domain;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Expense.Domain;
using Gggs.Modules.Helpdesk.Domain;
using Gggs.Modules.Rewards.Domain;
using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

public sealed class AssetTests
{
    private static Asset Laptop(decimal cost, DateOnly bought)
        => Asset.Register("AST-LAPT-0001", new AssetDetails(Guid.NewGuid(), "Laptop", null, null, "SN1", bought, cost, null, null, null, null), AssetCondition.New);

    [Fact]
    public void Book_value_falls_in_a_straight_line_and_stops_at_zero()
    {
        Asset laptop = Laptop(36000m, new DateOnly(2025, 1, 10));
        laptop.BookValue(new DateOnly(2025, 1, 31), 3).ShouldBe(36000m);
        laptop.BookValue(new DateOnly(2026, 1, 10), 3).ShouldBe(24000m);
        laptop.BookValue(new DateOnly(2029, 1, 1), 3).ShouldBe(0m);
        laptop.BookValue(new DateOnly(2026, 1, 10), 0).ShouldBe(36000m);
    }
}

public sealed class ExpenseTests
{
    [Fact]
    public void Policy_flags_lines_over_the_limit_or_without_a_needed_receipt()
    {
        var meals = ExpenseCategory.Create("MEALS", "Meals", 1500m, 500m);
        meals.PolicyCheck(1200m, hasReceipt: true).ShouldBeNull();
        meals.PolicyCheck(400m, hasReceipt: false).ShouldBeNull();
        meals.PolicyCheck(800m, hasReceipt: false).ShouldBe("receipt needed");
        meals.PolicyCheck(2000m, hasReceipt: false)!.ShouldContain("over the 1500 limit");
    }

    [Fact]
    public void An_out_of_policy_line_needs_a_justification()
    {
        var input = new ClaimLineInput(Guid.NewGuid(), new DateOnly(2026, 10, 1), "Dinner", 2000m, null, true, false, null);
        Should.Throw<DomainRuleException>(() => ExpenseLine.Add(Guid.NewGuid(), input, "over the 1500 limit", new DateOnly(2026, 10, 2))).ErrorCode.ShouldBe("EXPENSE_POLICY");
        ExpenseLine.Add(Guid.NewGuid(), input with { Justification = "Client dinner" }, "over the 1500 limit", new DateOnly(2026, 10, 2)).PolicyFlag.ShouldNotBeNull();
    }

    [Fact]
    public void A_settled_trip_owes_or_is_owed_the_difference_from_its_advance()
    {
        var trip = TravelRequest.Draft("TRV-2026-00001", Guid.NewGuid(), new TravelDetails("Workshop", "Pune", "Delhi", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), TravelMode.Air, 15000m, 6000m, null));
        trip.Submit();
        trip.Decide(approved: true);
        trip.PayAdvance(6000m, new DateOnly(2026, 9, 28));
        trip.Settle(9500m, new DateOnly(2026, 10, 6));
        trip.SettlementAmount.ShouldBe(3500m);
        trip.Status.ShouldBe(TravelStatus.Settled);
    }

    [Fact]
    public void An_empty_claim_cannot_be_submitted()
    {
        var claim = ExpenseClaim.Draft("EXP-2026-00001", Guid.NewGuid(), "Nothing yet", null, null, "INR");
        Should.Throw<DomainRuleException>(() => claim.Submit(DateTime.UtcNow)).ErrorCode.ShouldBe("CLAIM_EMPTY");
    }
}

public sealed class HelpdeskTests
{
    private static readonly DateTime Raised = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    private static Ticket Ticket(TicketPriority priority)
        => Modules.Helpdesk.Domain.Ticket.Raise("TKT-000001", Guid.NewGuid(), HelpdeskCategory.Create("IT", "IT", null, null, 4, 24, false), "VPN", "Drops", priority, Raised);

    [Theory]
    [InlineData(TicketPriority.Urgent, 1, 6)]
    [InlineData(TicketPriority.High, 2, 12)]
    [InlineData(TicketPriority.Medium, 4, 24)]
    [InlineData(TicketPriority.Low, 8, 48)]
    public void Priority_scales_the_category_targets(TicketPriority priority, int responseHours, int resolutionHours)
    {
        Ticket ticket = Ticket(priority);
        ticket.ResponseDueAt.ShouldBe(Raised.AddHours(responseHours));
        ticket.ResolutionDueAt.ShouldBe(Raised.AddHours(resolutionHours));
    }

    [Fact]
    public void Missed_targets_are_flagged_once_and_a_reply_meets_the_response_target()
    {
        Ticket ticket = Ticket(TicketPriority.Medium);
        ticket.CheckSla(Raised.AddHours(3)).ShouldBe((false, false));
        ticket.CheckSla(Raised.AddHours(5)).ShouldBe((true, false));
        ticket.CheckSla(Raised.AddHours(6)).ShouldBe((false, false));

        Ticket answered = Ticket(TicketPriority.Medium);
        answered.Responded(Raised.AddHours(1));
        answered.Status.ShouldBe(TicketStatus.InProgress);
        answered.CheckSla(Raised.AddHours(30)).ShouldBe((false, true));
    }

    [Fact]
    public void A_resolved_ticket_reopens_and_only_then_can_close()
    {
        Ticket ticket = Ticket(TicketPriority.High);
        Should.Throw<DomainRuleException>(() => ticket.MoveTo(TicketStatus.Resolved, null, Raised)).ErrorCode.ShouldBe("TICKET_RESOLUTION");
        ticket.MoveTo(TicketStatus.Resolved, "Updated the client", Raised.AddHours(20));
        ticket.ResolutionBreached.ShouldBeTrue();
        ticket.Reopen("Still dropping", Raised.AddHours(21));
        ticket.Status.ShouldBe(TicketStatus.InProgress);
        ticket.ReopenCount.ShouldBe(1);
        Should.Throw<DomainRuleException>(() => ticket.Close(Raised.AddHours(22))).ErrorCode.ShouldBe("TICKET_CLOSE");
    }
}

public sealed class EngagementTests
{
    private static readonly SurveyQuestion[] Questions =
    [
        new("enps", "Recommend us?", QuestionType.Enps),
        new("manager", "My manager supports me.", QuestionType.Rating),
        new("stay", "Staying?", QuestionType.Choice, ["Yes", "No"]),
        new("idea", "One idea", QuestionType.Text, Required: false),
    ];

    [Fact]
    public void ENps_is_promoters_less_detractors()
    {
        Enps.Of([10, 9, 8, 7, 6, 0]).ShouldBe((2, 2, 2, 0.0));
        Enps.Of([10, 10, 9, 3]).Score.ShouldBe(50.0);
        Enps.Of([]).Score.ShouldBeNull();
    }

    [Fact]
    public void Answers_are_checked_against_each_question()
    {
        var survey = Survey.Draft("Pulse", null, true, Questions, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));
        survey.Publish(new DateOnly(2026, 10, 1)).ShouldBeTrue();
        survey.Open(10);

        survey.CheckAnswers(new Dictionary<string, string> { ["enps"] = "9", ["manager"] = "4", ["stay"] = "Yes" }).Count.ShouldBe(3);
        Should.Throw<DomainRuleException>(() => survey.CheckAnswers(new Dictionary<string, string> { ["enps"] = "11", ["manager"] = "4", ["stay"] = "Yes" })).ErrorCode.ShouldBe("SURVEY_ANSWER_INVALID");
        Should.Throw<DomainRuleException>(() => survey.CheckAnswers(new Dictionary<string, string> { ["enps"] = "9", ["manager"] = "4", ["stay"] = "Maybe" })).ErrorCode.ShouldBe("SURVEY_ANSWER_INVALID");
        Should.Throw<DomainRuleException>(() => survey.CheckAnswers(new Dictionary<string, string> { ["enps"] = "9" })).ErrorCode.ShouldBe("SURVEY_ANSWER_REQUIRED");
    }

    [Fact]
    public void An_anonymous_response_records_no_employee()
    {
        var survey = Survey.Draft("Pulse", null, true, Questions, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));
        SurveyResponse.Record(survey, Guid.NewGuid(), Guid.NewGuid(), []).EmployeeId.ShouldBeNull();
        var named = Survey.Draft("Named", null, false, Questions, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));
        SurveyResponse.Record(named, Guid.NewGuid(), null, []).EmployeeId.ShouldNotBeNull();
    }

    [Fact]
    public void A_choice_question_needs_two_options()
        => Should.Throw<DomainRuleException>(() => Survey.Draft("Bad", null, false, [new SurveyQuestion("q", "Pick", QuestionType.Choice, ["Only"])], null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)))
            .ErrorCode.ShouldBe("SURVEY_QUESTION_OPTIONS");
}

public sealed class RewardTests
{
    private static readonly Guid Employee = Guid.NewGuid();

    [Fact]
    public void Spending_takes_the_soonest_expiring_points_first()
    {
        PointEntry later = PointEntry.Credit(Employee, PointEntryKind.Earned, 300, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), null, null, "a");
        PointEntry sooner = PointEntry.Credit(Employee, PointEntryKind.Earned, 200, new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 1), null, null, "b");
        PointEntry never = PointEntry.Credit(Employee, PointEntryKind.Adjusted, 100, new DateOnly(2025, 1, 1), null, null, null, "c");

        PointEntry.SpendFifo([later, sooner, never], 350);
        sooner.RemainingPoints.ShouldBe(0);
        later.RemainingPoints.ShouldBe(150);
        never.RemainingPoints.ShouldBe(100);
        Should.Throw<DomainRuleException>(() => PointEntry.SpendFifo([later, sooner, never], 251)).ErrorCode.ShouldBe("POINTS_INSUFFICIENT");
    }

    [Fact]
    public void Anniversary_points_can_scale_with_years_served()
    {
        var rule = RewardRule.Create("Anniversary", RuleTrigger.WorkAnniversary, 200, 1, true, "Happy {years} years");
        rule.PointsFor(3).ShouldBe(600);
        rule.Applies(0).ShouldBeFalse();
        RewardRule.Create("Review", RuleTrigger.ReviewRating, 1000, 4, true, "x").PerYear.ShouldBeFalse();
    }

    [Fact]
    public void Stock_runs_out_and_returns_on_cancel()
    {
        var item = CatalogItem.Create("Hoodie", null, "Merch", 1200, 1);
        item.Take();
        Should.Throw<DomainRuleException>(item.Take).ErrorCode.ShouldBe("CATALOG_OUT_OF_STOCK");
        item.PutBack();
        item.Stock.ShouldBe(1);
    }

    [Fact]
    public void Nobody_recognises_themselves_and_peer_types_carry_no_cash()
    {
        var type = RecognitionType.Create("THANKS", "Thanks", null, RecognitionKind.Peer, 50, false, null);
        Should.Throw<DomainRuleException>(() => Recognition.Give(Employee, Employee, type, "Me", true, null, new DateOnly(2026, 10, 1))).ErrorCode.ShouldBe("RECOGNITION_SELF");
        Should.Throw<DomainRuleException>(() => RecognitionType.Create("CASHY", "Cash", null, RecognitionKind.Peer, 50, false, 500m)).ErrorCode.ShouldBe("RECOGNITION_TYPE_CASH");
    }

    [Fact]
    public void Settings_give_the_expiry_date_of_new_points()
    {
        RewardSettings settings = RewardSettings.Default();
        settings.ExpiryFrom(new DateOnly(2026, 10, 19)).ShouldBe(new DateOnly(2027, 10, 19));
        settings.Update(1, "INR", 0, 500);
        settings.ExpiryFrom(new DateOnly(2026, 10, 19)).ShouldBeNull();
    }
}

public sealed class SeparationTests
{
    [Fact]
    public void Notice_shortfall_counts_the_days_not_served()
    {
        var resignation = SeparationCase.Open(Guid.NewGuid(), SeparationType.Resignation, "Relocating", new DateOnly(2026, 10, 1), 60, new DateOnly(2026, 10, 31));
        resignation.ShortfallDays.ShouldBe(30);
        resignation.ExitReasonCode.ShouldBe("RESIGNATION");
        resignation.Approve(new DateOnly(2026, 12, 15));
        resignation.ShortfallDays.ShouldBe(0);
    }

    [Fact]
    public void A_terminated_employee_is_not_eligible_for_rehire_by_default()
        => SeparationCase.Open(Guid.NewGuid(), SeparationType.Termination, "Misconduct", new DateOnly(2026, 10, 1), 0, new DateOnly(2026, 10, 1)).EligibleForRehire.ShouldBeFalse();

    [Fact]
    public void A_withdrawal_is_too_late_after_the_last_day()
    {
        var resignation = SeparationCase.Open(Guid.NewGuid(), SeparationType.Resignation, "x", new DateOnly(2026, 10, 1), 30, new DateOnly(2026, 10, 31));
        resignation.Approve(null);
        Should.Throw<DomainRuleException>(() => resignation.Withdraw(new DateOnly(2026, 11, 1))).ErrorCode.ShouldBe("SEPARATION_WITHDRAW");
        resignation.Withdraw(new DateOnly(2026, 10, 30));
        resignation.Status.ShouldBe(CaseStatus.Withdrawn);
    }

    [Fact]
    public void Settlement_arithmetic_follows_the_usual_formulae()
    {
        SettlementMath.LastMonthSalary(62000m, new DateOnly(2026, 10, 15)).ShouldBe(30000m);
        SettlementMath.LeaveEncashment(26000m, 12.5m).ShouldBe(12500m);
        SettlementMath.NoticeRecovery(60000m, 15).ShouldBe(30000m);
    }

    [Theory]
    [InlineData("2020-01-01", "2024-12-31", false, 0)]
    [InlineData("2020-01-01", "2025-01-01", true, 144231)]
    [InlineData("2020-01-01", "2025-08-01", true, 173077)]
    public void Gratuity_counts_a_part_year_over_six_months_and_needs_five_years(string joined, string lastDay, bool eligible, decimal amount)
    {
        (decimal _, decimal paid, bool qualifies) = SettlementMath.Gratuity(50000m, DateOnly.Parse(joined, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(lastDay, System.Globalization.CultureInfo.InvariantCulture), 15, 26, 5, 2000000);
        qualifies.ShouldBe(eligible);
        paid.ShouldBe(amount);
    }

    [Fact]
    public void A_settlement_totals_its_lines_and_locks_once_approved()
    {
        var settlement = Settlement.Start(Guid.NewGuid());
        settlement.Total(
        [
            SettlementLine.Add(settlement.Id, "SALARY", "Salary", false, 30000m, false, null),
            SettlementLine.Add(settlement.Id, "GRATUITY", "Gratuity", false, 144231m, false, null),
            SettlementLine.Add(settlement.Id, "NOTICE_RECOVERY", "Notice", true, 20000m, false, null),
        ]);
        settlement.Net.ShouldBe(154231m);
        settlement.Approve();
        Should.Throw<DomainRuleException>(() => settlement.Total([])).ErrorCode.ShouldBe("SETTLEMENT_NOT_DRAFT");
        Should.Throw<DomainRuleException>(() => SettlementLine.Add(settlement.Id, "X", "X", false, -1m, true, null)).ErrorCode.ShouldBe("SETTLEMENT_LINE");
    }
}
