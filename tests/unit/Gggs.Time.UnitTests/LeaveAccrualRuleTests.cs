using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Leave.Application;
using Gggs.Modules.Leave.Domain;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class AccrualScheduleTests
{
    private static readonly DateOnly LongAgo = new(2020, 1, 10);

    private static PlanItemRules Rules(decimal days, AccrualFrequency accrual) => new(
        days, accrual, null, 0, false, [], 0, true, true, null, null, null, 0, null, null);

    [Fact]
    public void Monthly_AtTheStart_KeepsTheKeysLedgersAlreadyHave()
    {
        AccrualCredit credit = AccrualSchedule.For(Rules(12, AccrualFrequency.Monthly), 1, LongAgo, new DateOnly(2026, 3, 1))!;
        credit.ShouldBe(new AccrualCredit("accrual:2026-03", new DateOnly(2026, 3, 1), 1m, "Accrual 2026-03"));
    }

    [Fact]
    public void Monthly_RespectsTheJoiningCutoff()
    {
        PlanItemRules rules = Rules(12, AccrualFrequency.Monthly) with { JoiningCutoffDay = 10 };
        AccrualSchedule.For(rules, 1, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1)).ShouldNotBeNull();
        AccrualSchedule.For(rules, 1, new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 1)).ShouldBeNull("joining after the cut-off earns from the next month");
    }

    [Fact]
    public void Quarterly_FollowsTheLeaveYear_AndIsCreditedOncePerQuarter()
    {
        PlanItemRules rules = Rules(12, AccrualFrequency.Quarterly);
        AccrualCredit q1 = AccrualSchedule.For(rules, 4, LongAgo, new DateOnly(2026, 5, 1))!;
        q1.PeriodKey.ShouldBe("accrual:FY2026-Q1");
        q1.Days.ShouldBe(3m);
        q1.EntryDate.ShouldBe(new DateOnly(2026, 4, 1));
        AccrualSchedule.For(rules, 4, LongAgo, new DateOnly(2027, 1, 1))!.PeriodKey.ShouldBe("accrual:FY2026-Q4");
    }

    [Fact]
    public void CreditingAtTheEnd_PaysThePeriodJustFinished_AndNothingMidPeriod()
    {
        PlanItemRules rules = Rules(12, AccrualFrequency.HalfYearly) with { CreditTiming = CreditTiming.EndOfPeriod };
        AccrualSchedule.For(rules, 1, LongAgo, new DateOnly(2026, 4, 1)).ShouldBeNull();
        AccrualCredit first = AccrualSchedule.For(rules, 1, LongAgo, new DateOnly(2026, 7, 1))!;
        first.PeriodKey.ShouldBe("accrual:2026-H1");
        first.EntryDate.ShouldBe(new DateOnly(2026, 6, 30));
        first.Days.ShouldBe(6m);
    }

    [Fact]
    public void AJoinerIsProrated_UnlessThePolicySaysOtherwise()
    {
        PlanItemRules yearly = Rules(12, AccrualFrequency.Yearly);
        DateOnly joinedInOctober = new(2026, 10, 1);
        AccrualSchedule.For(yearly, 1, joinedInOctober, new DateOnly(2026, 10, 1))!.Days.ShouldBe(3m);
        AccrualSchedule.For(yearly with { ProrateOnJoining = false }, 1, joinedInOctober, new DateOnly(2026, 10, 1))!.Days.ShouldBe(12m);
    }

    [Fact]
    public void PerOccurrenceAndManualLeave_NeverAccrue()
    {
        AccrualSchedule.For(Rules(12, AccrualFrequency.None), 1, LongAgo, new DateOnly(2026, 3, 1)).ShouldBeNull();
        AccrualSchedule.For(Rules(0, AccrualFrequency.None) with { GrantPerOccurrence = 182 }, 1, LongAgo, new DateOnly(2026, 3, 1)).ShouldBeNull();
    }
}

public sealed class YearEndTests
{
    private static PlanItemRules Rules(decimal? carry) => new(
        18, AccrualFrequency.Monthly, carry, 0, false, [], 0, true, true, null, null, null, 0, null, null);

    [Fact]
    public void TheExcess_IsEncashedUpToTheLimit_AndTheRestLapses()
    {
        YearEnd.Close(Rules(10) with { EncashAtYearEnd = true, MaxEncashDays = 5 }, 22).ShouldBe(new YearEndOutcome(5, 7, 10));
        YearEnd.Close(Rules(10), 22).ShouldBe(new YearEndOutcome(0, 12, 10));
        YearEnd.Close(Rules(10), 8).ShouldBe(new YearEndOutcome(0, 0, 8));
        YearEnd.Close(Rules(null), 40).ShouldBe(new YearEndOutcome(0, 0, 40), "no limit carries everything");
    }

    [Fact]
    public void CarriedDays_ExpireOnlyIfUnused()
    {
        YearEnd.Expiring(carried: 10, takenSinceYearStart: 4, currentBalance: 12).ShouldBe(6);
        YearEnd.Expiring(carried: 10, takenSinceYearStart: 12, currentBalance: 3).ShouldBe(0);
        YearEnd.Expiring(carried: 10, takenSinceYearStart: 0, currentBalance: 7).ShouldBe(7, "never more than the balance");
    }

    [Fact]
    public void YearEndRules_ThatCannotWork_AreRefused()
    {
        Should.Throw<DomainRuleException>(() => (Rules(null) with { EncashAtYearEnd = true }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_ENCASH");
        Should.Throw<DomainRuleException>(() => (Rules(0) with { CarryForwardExpiryMonths = 3 }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_EXPIRY");
    }
}

public sealed class LifetimeLeaveTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static PlanItemRules Maternity() => new(
        0, AccrualFrequency.None, null, 0, false, ["FEMALE"], 0, true, true, null, null, null, 0, null, null,
        GrantPerOccurrence: 182, MaxOccurrencesInEmployment: 2);

    private static readonly LeaveApplicant Her = new("FEMALE", new DateOnly(2020, 1, 1), null, new DateOnly(2020, 7, 1), OnNotice: false);

    [Fact]
    public void EachOccurrence_IsCappedAtItsGrant_AndCountedOverTheEmployment()
    {
        LeaveEligibility.Check("Maternity leave", Maternity(), Her, new LeaveAsk(Today, Today.AddDays(181), 182, Today, false, 0, OccurrencesSoFar: 1)).ShouldBeEmpty();
        LeaveEligibility.Check("Maternity leave", Maternity(), Her, new LeaveAsk(Today, Today.AddDays(199), 200, Today, false, 0, 0)).ShouldHaveSingleItem().ShouldContain("182 day(s) each time");
        LeaveEligibility.Check("Maternity leave", Maternity(), Her, new LeaveAsk(Today, Today.AddDays(9), 10, Today, false, 0, OccurrencesSoFar: 2)).ShouldHaveSingleItem().ShouldContain("2 time(s) in your employment");
    }

    [Fact]
    public void AGrantPerOccurrence_CannotAlsoAccrue()
        => Should.Throw<DomainRuleException>(() => (Maternity() with { Accrual = AccrualFrequency.Monthly }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_GRANT");
}

public sealed class LeaveDocumentTests
{
    [Fact]
    public void OnlyPdfsAndPhotos_AreAccepted_ByTheirFirstBytes()
    {
        LeaveService.SniffDocument("%PDF-1.7"u8).ShouldBe("application/pdf");
        LeaveService.SniffDocument([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        LeaveService.SniffDocument([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).ShouldBe("image/png");
        LeaveService.SniffDocument("MZ\u0090\u0000"u8).ShouldBeNull();
    }
}
