using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Leave.Domain;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class LeaveYearTests
{
    [Fact]
    public void AJanuaryYear_KeepsThePlainCalendarYearAsItsKey()
    {
        LeaveYear year = LeaveYear.For(1, new DateOnly(2026, 10, 31));
        year.ShouldBe(new LeaveYear(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "2026"));
    }

    [Fact]
    public void AnAprilYear_RunsAprilToMarch_AndIsNamedForTheYearItStarts()
    {
        LeaveYear.For(4, new DateOnly(2027, 2, 10)).ShouldBe(new LeaveYear(new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), "FY2026"));
        LeaveYear.For(4, new DateOnly(2026, 4, 1)).Key.ShouldBe("FY2026");
        LeaveYear.For(4, new DateOnly(2026, 3, 31)).Key.ShouldBe("FY2025");
        LeaveYear.Label(4).ShouldBe("Apr–Mar");
        LeaveYear.Label(1).ShouldBe("Jan–Dec");
    }

    [Fact]
    public void MonthsFrom_CountsTheJoiningMonthToTheYearsEnd()
    {
        LeaveYear april = LeaveYear.For(4, new DateOnly(2026, 6, 1));
        april.MonthsFrom(new DateOnly(2026, 4, 1)).ShouldBe(12);
        april.MonthsFrom(new DateOnly(2027, 1, 1)).ShouldBe(3);
        april.MonthsFrom(new DateOnly(2025, 1, 1)).ShouldBe(12, "someone who joined before the year gets all of it");
    }
}

public sealed class LeavePlanResolverTests
{
    private static readonly Guid Standard = Guid.NewGuid();
    private static readonly Guid Contract = Guid.NewGuid();
    private static readonly Guid Senior = Guid.NewGuid();

    private static readonly Dictionary<string, Guid> Assignments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FULL_TIME"] = Standard,
        ["CONTRACT"] = Contract,
    };

    [Fact]
    public void TheEngagementTypesPolicy_Applies_WhenThereIsNoException()
        => LeavePlanResolver.Resolve([], Assignments, "contract", new DateOnly(2026, 5, 1)).ShouldBe(new PlanChoice(Contract, PlanSource.EngagementType));

    [Fact]
    public void AnExceptionCoveringTheDay_WinsOverTheEngagementType()
    {
        EmployeeLeavePlan exception = EmployeeLeavePlan.Create(Guid.NewGuid(), Senior, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30), "Senior hire");

        LeavePlanResolver.Resolve([exception], Assignments, "FULL_TIME", new DateOnly(2026, 6, 1)).ShouldBe(new PlanChoice(Senior, PlanSource.Exception));
        LeavePlanResolver.Resolve([exception], Assignments, "FULL_TIME", new DateOnly(2026, 10, 1)).ShouldBe(new PlanChoice(Standard, PlanSource.EngagementType), "after it ends, the engagement type's policy is back");
    }

    [Fact]
    public void AnEngagementTypeWithoutAPolicy_ResolvesToNone()
        => LeavePlanResolver.Resolve([], Assignments, "INTERN", new DateOnly(2026, 5, 1)).ShouldBeNull();

    [Fact]
    public void OverlappingExceptions_AreDetected()
    {
        EmployeeLeavePlan open = EmployeeLeavePlan.Create(Guid.NewGuid(), Senior, new DateOnly(2026, 4, 1), null, null);
        open.Overlaps(new DateOnly(2030, 1, 1), null).ShouldBeTrue();
        EmployeeLeavePlan closed = EmployeeLeavePlan.Create(Guid.NewGuid(), Senior, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), null);
        closed.Overlaps(new DateOnly(2026, 7, 1), null).ShouldBeFalse();
        Should.Throw<DomainRuleException>(() => EmployeeLeavePlan.Create(Guid.NewGuid(), Senior, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1), null));
    }
}

public sealed class LeaveEligibilityTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static PlanItemRules Open() => new(
        12, AccrualFrequency.Monthly, null, 0, false, [], 0, AllowDuringProbation: true, AllowDuringNotice: true,
        null, null, null, 0, null, null);

    private static readonly LeaveApplicant Confirmed = new("FEMALE", new DateOnly(2020, 1, 1), null, new DateOnly(2020, 7, 1), OnNotice: false);

    private static LeaveAsk Ask(DateOnly start, decimal days = 1, bool onBehalf = false, int requests = 0) => new(start, start.AddDays((int)days - 1), days, Today, onBehalf, requests);

    [Fact]
    public void OpenRules_RaiseNothing()
        => LeaveEligibility.Check("Casual leave", Open(), Confirmed, Ask(Today.AddDays(5))).ShouldBeEmpty();

    [Fact]
    public void GenderAndService_DecideWhoCanTakeIt()
    {
        PlanItemRules paternity = Open() with { EligibleGenderCodes = ["MALE"] };
        LeaveEligibility.Check("Paternity leave", paternity, Confirmed, Ask(Today.AddDays(5))).ShouldHaveSingleItem().ShouldContain("not available");

        PlanItemRules afterAYear = Open() with { MinServiceMonths = 12 };
        LeaveApplicant newJoiner = Confirmed with { JoiningDate = new DateOnly(2026, 6, 1) };
        LeaveEligibility.Check("Earned leave", afterAYear, newJoiner, Ask(Today.AddDays(5))).ShouldHaveSingleItem().ShouldContain("12 month(s) of service");
    }

    [Fact]
    public void ProbationAndNotice_AreCheckedOnlyWhereThePolicySays()
    {
        LeaveApplicant onProbation = Confirmed with { ConfirmationDate = null, ProbationEndDate = Today.AddDays(30) };
        LeaveEligibility.Check("Earned leave", Open() with { AllowDuringProbation = false }, onProbation, Ask(Today.AddDays(5))).ShouldHaveSingleItem().ShouldContain("probation");

        LeaveApplicant leaving = Confirmed with { OnNotice = true };
        LeaveEligibility.Check("Earned leave", Open() with { AllowDuringNotice = false }, leaving, Ask(Today.AddDays(5))).ShouldHaveSingleItem().ShouldContain("notice period");
        LeaveEligibility.Check("Sick leave", Open(), leaving, Ask(Today.AddDays(5))).ShouldBeEmpty();
    }

    [Fact]
    public void RequestLimits_CoverLengthAndHowOften()
    {
        PlanItemRules limited = Open() with { MinDaysPerRequest = 2, MaxConsecutiveDays = 5, MaxRequestsPerYear = 3 };
        LeaveEligibility.Check("Earned leave", limited, Confirmed, Ask(Today.AddDays(5), days: 1)).ShouldHaveSingleItem().ShouldContain("at least 2");
        LeaveEligibility.Check("Earned leave", limited, Confirmed, Ask(Today.AddDays(5), days: 6)).ShouldHaveSingleItem().ShouldContain("at most 5");
        LeaveEligibility.Check("Earned leave", limited, Confirmed, Ask(Today.AddDays(5), days: 2, requests: 3)).ShouldHaveSingleItem().ShouldContain("3 time(s)");
    }

    [Fact]
    public void NoticeBackdatingAndAdvance_BindTheEmployee_ButNotHrApplyingForThem()
    {
        PlanItemRules windowed = Open() with { MinNoticeDays = 7, MaxBackdatedDays = 3, MaxAdvanceDays = 90 };
        LeaveEligibility.Check("Earned leave", windowed, Confirmed, Ask(Today.AddDays(2))).ShouldHaveSingleItem().ShouldContain("7 day(s) of notice");
        LeaveEligibility.Check("Sick leave", windowed with { MinNoticeDays = 0 }, Confirmed, Ask(Today.AddDays(-5))).ShouldHaveSingleItem().ShouldContain("3 day(s) after");
        LeaveEligibility.Check("Earned leave", windowed, Confirmed, Ask(Today.AddDays(120))).ShouldHaveSingleItem().ShouldContain("90 day(s) ahead");

        LeaveEligibility.Check("Earned leave", windowed, Confirmed, Ask(Today.AddDays(-5), onBehalf: true)).ShouldBeEmpty();
    }
}

public sealed class LeavePlanTests
{
    private static PlanItemRules Rules(decimal days = 12) => new(
        days, AccrualFrequency.Monthly, 0, 0, false, [], 0, true, true, null, null, null, 0, null, null);

    [Fact]
    public void SetItems_AddsUpdatesAndRemoves_InOneGo()
    {
        Guid casual = Guid.NewGuid(), sick = Guid.NewGuid(), earned = Guid.NewGuid();
        LeavePlan plan = LeavePlan.Create("ft", "Full-time", null, 4);
        plan.Code.ShouldBe("FT");
        plan.SetItems([(casual, Rules()), (sick, Rules())]);

        IReadOnlyList<LeavePlanItem> removed = plan.SetItems([(casual, Rules(10)), (earned, Rules(18))]);

        removed.ShouldHaveSingleItem().LeaveTypeId.ShouldBe(sick);
        plan.Items.Select(item => item.LeaveTypeId).ShouldBe([casual, earned], ignoreOrder: true);
        plan.ItemFor(casual)!.AnnualEntitlement.ShouldBe(10);
    }

    [Fact]
    public void ALeaveTypeAppearsOnce_AndTheLeaveYearStartsInARealMonth()
    {
        Guid casual = Guid.NewGuid();
        LeavePlan plan = LeavePlan.Create("FT", "Full-time", null, 1);
        Should.Throw<DomainRuleException>(() => plan.SetItems([(casual, Rules()), (casual, Rules())])).ErrorCode.ShouldBe("LEAVE_PLAN_DUPLICATE_TYPE");
        Should.Throw<DomainRuleException>(() => LeavePlan.Create("FT", "Full-time", null, 13)).ErrorCode.ShouldBe("LEAVE_PLAN_YEAR_START");
    }

    [Fact]
    public void Rules_ThatContradictThemselves_AreRefused()
    {
        Should.Throw<DomainRuleException>(() => (Rules() with { MinDaysPerRequest = 5, MaxConsecutiveDays = 3 }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_MIN_DAYS");
        Should.Throw<DomainRuleException>(() => (Rules() with { MinDaysPerRequest = 1.3m }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_MIN_DAYS");
        Should.Throw<DomainRuleException>(() => (Rules() with { MinNoticeDays = 30, MaxAdvanceDays = 10 }).Validate()).ErrorCode.ShouldBe("LEAVE_POLICY_WINDOW");
    }

    [Fact]
    public void GenderCodes_AreStoredOnceEach_InUpperCase()
    {
        LeavePlan plan = LeavePlan.Create("FT", "Full-time", null, 1);
        Guid maternity = Guid.NewGuid();
        plan.SetItems([(maternity, Rules(0) with { EligibleGenderCodes = ["female", "FEMALE", " "] })]);
        plan.ItemFor(maternity)!.Genders.ShouldBe(["FEMALE"]);
    }
}
