using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Resources.Domain;
using Gggs.Modules.Skills.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Timesheets.Domain;
using Shouldly;

namespace Gggs.Delivery.UnitTests;

public sealed class CapacityTests
{
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static List<DateOnly> Weekdays(DateOnly from, int count) => [.. Enumerable.Range(0, count).Select(from.AddDays).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];

    [Fact]
    public void TheSpecExample_AddsUpToTenPercentAvailable()
    {
        // Project A 50%, project B 30%, leave the equivalent of 10%: 10% available (spec section 59).
        List<DateOnly> days = Weekdays(Monday, 14);
        var leave = new Dictionary<DateOnly, decimal> { [days[0]] = 1m };
        CapacityCell cell = Capacity.For(days[0], days[^1], days, leave,
            [new AllocationSpan(ProjectA, "A", 50, Monday, null, true), new AllocationSpan(ProjectB, "B", 30, Monday, null, false)]);

        cell.AllocatedPercent.ShouldBe(80);
        cell.BillablePercent.ShouldBe(50);
        cell.LeavePercent.ShouldBe(10);
        cell.AvailablePercent.ShouldBe(10);
        cell.State.ShouldBe(CapacityState.PartiallyAvailable);
    }

    [Fact]
    public void AnAllocationCoveringHalfThePeriod_CountsHalf()
    {
        List<DateOnly> days = Weekdays(Monday, 14);
        CapacityCell cell = Capacity.For(days[0], days[^1], days, new Dictionary<DateOnly, decimal>(), [new AllocationSpan(ProjectA, "A", 100, Monday, Monday.AddDays(4), true)]);
        cell.AllocatedPercent.ShouldBe(50);
    }

    [Fact]
    public void OverAHundredPercent_IsOverallocated_AndNothingIsBench()
    {
        List<DateOnly> days = Weekdays(Monday, 7);
        Capacity.For(days[0], days[^1], days, new Dictionary<DateOnly, decimal>(), [new AllocationSpan(ProjectA, "A", 60, Monday, null, true), new AllocationSpan(ProjectB, "B", 50, Monday, null, true)])
            .State.ShouldBe(CapacityState.Overallocated);
        Capacity.For(days[0], days[^1], days, new Dictionary<DateOnly, decimal>(), []).State.ShouldBe(CapacityState.Bench);
    }

    [Fact]
    public void ThePeak_IsTheBusiestDay_NotTheAverage()
    {
        List<AllocationSpan> spans =
        [
            new(ProjectA, "A", 60, Monday, Monday.AddDays(9), true),
            new(ProjectB, "B", 50, Monday.AddDays(7), null, true),
        ];

        Capacity.PeakAllocation(Monday, Monday.AddDays(30), spans).ShouldBe(110);
        Capacity.PeakAllocation(Monday.AddDays(10), Monday.AddDays(30), spans).ShouldBe(50);
    }
}

public sealed class MatchingTests
{
    private static readonly Guid Dotnet = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();

    private static ResourceRequest Request(decimal? maxCost = null, Guid? location = null) => ResourceRequest.Raise(Guid.NewGuid(), null, new RequestDetails(
        null, "Senior .NET", [new SkillRequirement(Dotnet, 4)], 5, 1, 100, new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31), true, location, null, maxCost, RequestPriority.High, null));

    private static CandidateFacts Candidate(int level, decimal years, int peak, decimal? cost = null, DateOnly? exit = null, Guid? location = null)
        => new(Guid.NewGuid(), new Dictionary<Guid, int> { [Dotnet] = level }, new Dictionary<Guid, decimal> { [Dotnet] = years }, 3, peak, location, "HYBRID", cost, true, exit);

    private static readonly Dictionary<Guid, string> Names = new() { [Dotnet] = ".NET" };

    [Fact]
    public void ThePerfectCandidate_ScoresAHundred()
        => Matching.Score(Request(), Candidate(5, 8, 0), Names).Overall.ShouldBe(100);

    [Fact]
    public void SkillsExperienceAndAvailability_EachPullTheScoreDown()
    {
        MatchScore score = Matching.Score(Request(), Candidate(level: 2, years: 2.5m, peak: 50), Names);
        score.Skills.ShouldBe(50);
        score.Experience.ShouldBe(50);
        score.Availability.ShouldBe(50);
        score.Overall.ShouldBe(60);
    }

    [Fact]
    public void AMissingSkill_IsNamed_AndScoresZero()
    {
        var facts = new CandidateFacts(Guid.NewGuid(), new Dictionary<Guid, int>(), new Dictionary<Guid, decimal>(), 1, 0, null, null, null, true, null);
        MatchScore score = Matching.Score(Request(), facts, Names);
        score.Skills.ShouldBe(0);
        score.MissingSkills.ShouldBe([".NET"]);
    }

    [Fact]
    public void LeavingHalfwayThrough_HalvesAvailability()
    {
        var request = Request();
        DateOnly halfway = request.StartDate.AddDays((request.EndDate.DayNumber - request.StartDate.DayNumber) / 2);
        Matching.Score(request, Candidate(5, 8, 0, exit: halfway), Names).Availability.ShouldBeInRange(49, 51);
    }

    [Fact]
    public void CostAboveBudget_AndAnotherLocation_CostSomething()
    {
        MatchScore score = Matching.Score(Request(maxCost: 1000, location: Office), Candidate(5, 8, 0, cost: 2000, location: Guid.NewGuid()), Names);
        score.Cost.ShouldBe(50);
        score.Location.ShouldBe(70);
    }

    [Fact]
    public void ARequest_IsFulfilledSeatBySeat()
    {
        ResourceRequest request = ResourceRequest.Raise(Guid.NewGuid(), null, new RequestDetails(
            null, "QA", [], null, 2, 100, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), true, null, null, null, RequestPriority.Medium, null));
        request.Fulfil();
        request.Status.ShouldBe(RequestStatus.PartiallyFulfilled);
        request.Fulfil();
        request.Status.ShouldBe(RequestStatus.Fulfilled);
        Should.Throw<DomainRuleException>(() => request.Fulfil());
    }
}

public sealed class TimesheetRuleTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static List<DayFacts> Week(string? holidayOnWednesday = null, decimal leaveOnFriday = 0) =>
    [
        .. Enumerable.Range(0, 7).Select(offset =>
        {
            DateOnly date = Monday.AddDays(offset);
            bool working = offset < 5;
            return new DayFacts(date, working, offset == 2 ? holidayOnWednesday : null, offset == 4 ? leaveOnFriday : 0);
        }),
    ];

    private static IEnumerable<(DateOnly, decimal)> Hours(params decimal[] perDay) => perDay.Select((hours, offset) => (Monday.AddDays(offset), hours));

    [Fact]
    public void AFullWeek_RaisesNothing()
        => TimesheetChecks.Check(TimesheetRules.Default, Week(), Hours(8, 8, 8, 8, 8)).ShouldBeEmpty();

    [Fact]
    public void ADayOverTheMaximum_IsAnError_AndExtraHoursAreOvertime()
    {
        IReadOnlyList<TimesheetIssue> issues = TimesheetChecks.Check(TimesheetRules.Default, Week(), Hours(13, 8, 8, 8, 8));
        issues.ShouldContain(issue => issue.Level == IssueLevel.Error && issue.Code == "TIMESHEET_DAY_MAX");
        TimesheetChecks.Overtime(TimesheetRules.Default, Week(), Hours(13, 8, 8, 8, 8)).ShouldBe(5);
    }

    [Fact]
    public void HolidaysAndLeave_LowerTheWeeklyMinimum()
    {
        // A holiday Wednesday and a day's leave Friday: 24 hours is a full week.
        TimesheetChecks.Check(TimesheetRules.Default, Week("Diwali", 1), Hours(8, 8, 0, 8, 0)).ShouldBeEmpty();
    }

    [Fact]
    public void AShortWeek_IsWarned_OrBlockedWhenTheTenantInsists()
    {
        TimesheetChecks.Check(TimesheetRules.Default, Week(), Hours(8, 8, 8, 8)).ShouldContain(issue => issue.Code == "TIMESHEET_WEEK_SHORT" && issue.Level == IssueLevel.Warning);
        TimesheetChecks.Check(TimesheetRules.Default with { EnforceWeeklyMinimum = true }, Week(), Hours(8, 8, 8, 8))
            .ShouldContain(issue => issue.Code == "TIMESHEET_WEEK_SHORT" && issue.Level == IssueLevel.Error);
    }

    [Fact]
    public void WeekendHours_AreRefusedWhenTheTenantSaysSo()
    {
        IEnumerable<(DateOnly, decimal)> withSaturday = Hours(8, 8, 8, 8, 8, 4);
        TimesheetChecks.Check(TimesheetRules.Default, Week(), withSaturday).ShouldNotContain(issue => issue.Level == IssueLevel.Error);
        TimesheetChecks.Check(TimesheetRules.Default with { AllowWeekendEntries = false }, Week(), withSaturday).ShouldContain(issue => issue.Code == "TIMESHEET_WEEKEND");
    }

    [Fact]
    public void Entries_StayInTheWeek_InQuarterHours_WithTheirProject()
    {
        Timesheet sheet = Timesheet.Open(Guid.NewGuid(), Monday);
        Should.Throw<DomainRuleException>(() => TimesheetEntry.Book(sheet, Monday.AddDays(7), null, null, HourCategory.Internal, 8, null, null));
        Should.Throw<DomainRuleException>(() => TimesheetEntry.Book(sheet, Monday, null, null, HourCategory.Internal, 1.1m, null, null));
        Should.Throw<DomainRuleException>(() => TimesheetEntry.Book(sheet, Monday, null, null, HourCategory.Billable, 8, null, null));
        TimesheetEntry.Book(sheet, Monday, null, null, HourCategory.Training, 1.25m, null, null).Hours.ShouldBe(1.25m);
        Should.Throw<DomainRuleException>(() => Timesheet.Open(Guid.NewGuid(), Monday.AddDays(1)));
    }

    [Fact]
    public void Lateness_CountsFromTheWeekEndPlusTheGraceDays()
    {
        DateOnly sunday = Monday.AddDays(6);
        TimesheetChecks.IsLate(sunday, sunday.AddDays(2), 2).ShouldBeFalse();
        TimesheetChecks.IsLate(sunday, sunday.AddDays(3), 2).ShouldBeTrue();
    }

    [Fact]
    public void ASkillGap_IsTargetMinusCurrent()
    {
        EmployeeSkill skill = EmployeeSkill.Record(Guid.NewGuid(), Guid.NewGuid());
        skill.Assess(2, 4, 1, true, AssessmentSource.Manager, null, Monday);
        skill.Gap.ShouldBe(2);
        Should.Throw<DomainRuleException>(() => skill.Assess(6, null, null, false, AssessmentSource.Self, null, Monday));
    }
}
