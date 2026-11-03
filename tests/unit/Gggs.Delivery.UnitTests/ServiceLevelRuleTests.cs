using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Project.Domain;
using Shouldly;

namespace Gggs.Delivery.UnitTests;

public sealed class ServiceLevelRuleTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly SlaCalendar Office = new(false, new TimeOnly(9, 0), new TimeOnly(18, 0), [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);
    private static readonly SlaTarget Critical = new(WorkPriority.Critical, 30, 240, 5);
    private static readonly SlaTarget Low = new(WorkPriority.Low, 480, 2880, 1);

    // 2 November 2026 is a Monday.
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static TicketSla Ticket(SlaTarget? target = null, DateTime? opened = null, SlaCalendar? calendar = null)
        => TicketSla.Open(Guid.NewGuid(), Guid.NewGuid(), target ?? Critical, opened ?? At(2, 10), calendar ?? SlaCalendar.AllTheTime, Utc);

    // ----- The clock ------------------------------------------------------------------------------

    [Fact]
    public void AllTheTime_CountsEveryMinute()
        => SlaClock.Add(At(6, 23), 120, SlaCalendar.AllTheTime, Utc).ShouldBe(At(7, 1));

    [Fact]
    public void InWorkingHours_TimeWithinTheDayIsSimple()
        => SlaClock.Add(At(2, 10), 60, Office, Utc).ShouldBe(At(2, 11));

    [Fact]
    public void InWorkingHours_WhatDoesNotFitRunsIntoTheNextMorning()
        => SlaClock.Add(At(2, 17), 120, Office, Utc).ShouldBe(At(3, 10));

    [Fact]
    public void AfterHours_TheClockStartsTheNextMorning()
        => SlaClock.Add(At(2, 20), 30, Office, Utc).ShouldBe(At(3, 9, 30));

    [Fact]
    public void BeforeHours_TheClockStartsWhenTheDayDoes()
        => SlaClock.Add(At(2, 6), 30, Office, Utc).ShouldBe(At(2, 9, 30));

    [Fact]
    public void Weekends_DoNotCount()
        => SlaClock.Add(At(6, 17), 120, Office, Utc).ShouldBe(At(9, 10), "an hour on Friday, an hour on Monday");

    [Fact]
    public void ExactlyTheEndOfTheDay_IsStillThatDay()
        => SlaClock.Add(At(2, 17), 60, Office, Utc).ShouldBe(At(2, 18));

    [Fact]
    public void TheWorkingDay_IsTheOrganisationsNotUtcs()
    {
        // 09:00 in India is 03:30 UTC.
        TimeZoneInfo india = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        SlaClock.Add(At(2, 3, 30), 60, Office, india).ShouldBe(At(2, 4, 30));
        SlaClock.Add(At(2, 1), 60, Office, india).ShouldBe(At(2, 4, 30), "it is before opening there");
    }

    [Fact]
    public void TimeBetween_CountsOnlyWorkingTime()
    {
        SlaClock.Between(At(2, 10), At(2, 12), Office, Utc).ShouldBe(120);
        SlaClock.Between(At(2, 17), At(3, 10), Office, Utc).ShouldBe(120);
        SlaClock.Between(At(6, 17), At(9, 10), Office, Utc).ShouldBe(120, "across a weekend");
        SlaClock.Between(At(7, 10), At(8, 10), Office, Utc).ShouldBe(0, "all weekend");
        SlaClock.Between(At(3, 10), At(2, 10), Office, Utc).ShouldBe(0);
        SlaClock.Between(At(7, 10), At(7, 12), SlaCalendar.AllTheTime, Utc).ShouldBe(120);
    }

    // ----- A ticket -------------------------------------------------------------------------------

    [Fact]
    public void Opening_SetsBothTargets()
    {
        TicketSla ticket = Ticket();

        ticket.ResponseDueAt.ShouldBe(At(2, 10, 30));
        ticket.ResolutionDueAt.ShouldBe(At(2, 14));
    }

    [Fact]
    public void Answering_InTime_IsNoBreach()
    {
        TicketSla ticket = Ticket();

        ticket.Respond(At(2, 10, 20));

        ticket.IsBreached.ShouldBeFalse();
        ticket.CheckBreach(At(2, 11)).ShouldBe((false, false));
    }

    [Fact]
    public void AnsweringLate_IsABreach_DatedWhenItWasDue()
    {
        TicketSla ticket = Ticket();

        ticket.Respond(At(2, 12));

        ticket.ResponseBreachedAt.ShouldBe(At(2, 10, 30));
    }

    [Fact]
    public void TheSweep_RecordsEachTargetOnce()
    {
        TicketSla ticket = Ticket();

        ticket.CheckBreach(At(2, 11)).ShouldBe((true, false));
        ticket.CheckBreach(At(2, 12)).ShouldBe((false, false));
        ticket.CheckBreach(At(2, 15)).ShouldBe((false, true));
        ticket.CheckBreach(At(2, 16)).ShouldBe((false, false));
    }

    [Fact]
    public void WaitingOnTheClient_StopsTheResolutionClock()
    {
        TicketSla ticket = Ticket();
        ticket.Respond(At(2, 10, 10));

        ticket.Pause(At(2, 11));
        ticket.CheckBreach(At(2, 20)).ShouldBe((false, false), "it is with the client");

        ticket.Resume(At(2, 13), SlaCalendar.AllTheTime, Utc);
        ticket.PausedMinutes.ShouldBe(120);
        ticket.ResolutionDueAt.ShouldBe(At(2, 16));
    }

    [Fact]
    public void WaitingOnTheClient_DoesNotStopTheResponseClock()
    {
        TicketSla ticket = Ticket();

        ticket.Pause(At(2, 10, 5));

        ticket.CheckBreach(At(2, 11)).ShouldBe((true, false));
    }

    [Fact]
    public void Pausing_InWorkingHours_MovesTheTargetByWorkingTimeOnly()
    {
        TicketSla ticket = Ticket(opened: At(6, 10), calendar: Office);
        ticket.ResolutionDueAt.ShouldBe(At(6, 14));

        ticket.Pause(At(6, 17));
        ticket.Resume(At(9, 10), Office, Utc);

        ticket.PausedMinutes.ShouldBe(120);
        ticket.ResolutionDueAt.ShouldBe(At(6, 16));
    }

    [Fact]
    public void Resolving_Late_IsABreach_AndResolvingAlsoAnswers()
    {
        TicketSla ticket = Ticket();

        ticket.Resolve(At(2, 15), SlaCalendar.AllTheTime, Utc);

        ticket.RespondedAt.ShouldBe(At(2, 15));
        ticket.ResolutionBreachedAt.ShouldBe(At(2, 14));
        ticket.ResponseBreachedAt.ShouldBe(At(2, 10, 30));
    }

    [Fact]
    public void Resolving_WhilePaused_CountsThePauseFirst()
    {
        TicketSla ticket = Ticket();
        ticket.Respond(At(2, 10, 10));
        ticket.Pause(At(2, 11));

        ticket.Resolve(At(2, 16), SlaCalendar.AllTheTime, Utc);

        ticket.ResolutionBreachedAt.ShouldBeNull("five hours with the client move a 14:00 target to 19:00");
    }

    [Fact]
    public void Reopening_LeavesOutTheTimeItWasResolved()
    {
        TicketSla ticket = Ticket();
        ticket.Resolve(At(2, 11), SlaCalendar.AllTheTime, Utc);

        ticket.Reopen(At(2, 13), SlaCalendar.AllTheTime, Utc);

        ticket.ResolvedAt.ShouldBeNull();
        ticket.ResolutionDueAt.ShouldBe(At(2, 16));
    }

    [Fact]
    public void ANewPriority_SetsNewTargetsFromWhenItOpened()
    {
        TicketSla ticket = Ticket(Low);
        ticket.ResolutionDueAt.ShouldBe(At(4, 10));

        ticket.Retarget(Critical, SlaCalendar.AllTheTime, Utc);

        ticket.Priority.ShouldBe(WorkPriority.Critical);
        ticket.ResolutionDueAt.ShouldBe(At(2, 14));
        ticket.CreditPercent.ShouldBe(5);
    }

    [Fact]
    public void ABreachOnRecord_StaysWhenThePriorityDrops()
    {
        TicketSla ticket = Ticket();
        ticket.CheckBreach(At(2, 11));

        ticket.Retarget(Low, SlaCalendar.AllTheTime, Utc);

        ticket.ResponseBreachedAt.ShouldNotBeNull();
    }

    [Fact]
    public void OnlyABreach_CanBeExcluded_AndItNeedsAReason()
    {
        Should.Throw<DomainRuleException>(() => Ticket().Exclude("Nothing wrong", null)).ErrorCode.ShouldBe("SLA_NOT_BREACHED");

        TicketSla ticket = Ticket();
        ticket.CheckBreach(At(2, 11));
        Should.Throw<Exception>(() => ticket.Exclude(" ", null));

        ticket.Exclude("Client's own outage", null);
        ticket.CountsAgainst.ShouldBeFalse();
        ticket.Reinstate();
        ticket.CountsAgainst.ShouldBeTrue();
    }

    // ----- The policy -----------------------------------------------------------------------------

    private static SlaPolicyDetails Details(SlaCalendar? calendar = null, IReadOnlyList<SlaTarget>? targets = null, decimal cap = 15)
        => new(calendar ?? Office, targets ?? [Critical, Low], [], cap);

    [Fact]
    public void APolicy_ReadsBackWhatWasSaved()
    {
        Guid column = Guid.NewGuid();
        ProjectSlaPolicy policy = ProjectSlaPolicy.For(Guid.NewGuid(), Details() with { PausedColumnIds = [column] });

        policy.Targets.Count.ShouldBe(2);
        policy.Targets[0].ShouldBe(Critical);
        policy.PausedColumnIds.ShouldBe([column]);
        policy.Calendar.WorkingDays.Count.ShouldBe(5);
        policy.Calendar.DayStart.ShouldBe(new TimeOnly(9, 0));
    }

    [Fact]
    public void TheDefaults_AreAValidPolicy()
        => ProjectSlaPolicy.For(Guid.NewGuid(), SlaDefaults.Details).Targets.Count.ShouldBe(4);

    [Theory]
    [InlineData(18, 9, 1)]
    [InlineData(9, 18, 0)]
    public void ABadCalendar_IsRefused(int start, int end, int days)
    {
        SlaCalendar calendar = new(false, new TimeOnly(start, 0), new TimeOnly(end, 0), [.. Enumerable.Repeat(DayOfWeek.Monday, days)]);

        Should.Throw<DomainRuleException>(() => ProjectSlaPolicy.For(Guid.NewGuid(), Details(calendar))).ErrorCode.ShouldBe("SLA_CALENDAR");
    }

    [Theory]
    [InlineData(0, 60, 1, "SLA_TARGETS")]
    [InlineData(120, 60, 1, "SLA_TARGETS")]
    [InlineData(60, 200000, 1, "SLA_TARGETS")]
    [InlineData(60, 120, 101, "SLA_CREDITS")]
    [InlineData(60, 120, -1, "SLA_CREDITS")]
    public void ABadTarget_IsRefused(int response, int resolution, int credit, string code)
    {
        SlaTarget[] targets = [new SlaTarget(WorkPriority.High, response, resolution, credit)];

        Should.Throw<DomainRuleException>(() => ProjectSlaPolicy.For(Guid.NewGuid(), Details(targets: targets))).ErrorCode.ShouldBe(code);
    }

    [Fact]
    public void ThePriorities_MustNotRepeat_AndOneIsNeeded()
    {
        Should.Throw<DomainRuleException>(() => ProjectSlaPolicy.For(Guid.NewGuid(), Details(targets: [Critical, Critical]))).ErrorCode.ShouldBe("SLA_TARGETS");
        Should.Throw<DomainRuleException>(() => ProjectSlaPolicy.For(Guid.NewGuid(), Details(targets: []))).ErrorCode.ShouldBe("SLA_TARGETS");
    }

    // ----- The month ------------------------------------------------------------------------------

    private static readonly DateOnly November = new(2026, 11, 1);

    private static TicketSla Breached(SlaTarget? target = null)
    {
        TicketSla ticket = Ticket(target);
        ticket.CheckBreach(At(30, 23));
        return ticket;
    }

    [Fact]
    public void Credits_AddUpInAMonth_UpToTheCap()
    {
        IReadOnlyList<SlaCreditMonth> months = SlaReporting.Credits([Breached(), Breached(), Breached(), Breached()], Utc, 15);

        months.Single().Percent.ShouldBe(15m, "four at five percent is twenty, capped");
        months.Single().Breaches.ShouldBe(4);
    }

    [Fact]
    public void ATicket_EarnsOneCredit_ThoughItMissedBothTargets()
    {
        TicketSla both = Breached();
        both.ResponseBreachedAt.ShouldNotBeNull();
        both.ResolutionBreachedAt.ShouldNotBeNull();

        SlaReporting.Credits([both], Utc, 100).Single().Percent.ShouldBe(5m);
    }

    [Fact]
    public void AnExcludedBreach_EarnsNothing()
    {
        TicketSla excluded = Breached();
        excluded.Exclude("Client's own outage", null);

        SlaReporting.Credits([excluded], Utc, 100).ShouldBeEmpty();
    }

    [Fact]
    public void ACredit_BelongsToTheMonthTheTargetWasMissed_Locally()
    {
        // 18:30 UTC on 31 October is already November in India.
        TicketSla ticket = TicketSla.Open(Guid.NewGuid(), Guid.NewGuid(), Critical, new DateTime(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc), SlaCalendar.AllTheTime, Utc);
        ticket.CheckBreach(At(1, 0));
        TimeZoneInfo india = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        SlaReporting.Credits([ticket], Utc, 100).Single().Month.ShouldBe(new DateOnly(2026, 10, 1));
        SlaReporting.Credits([ticket], india, 100).Single().Month.ShouldBe(November);
    }

    [Fact]
    public void TheReport_MeasuresWhatWasDecidedInTheMonth()
    {
        TicketSla good = Ticket();
        good.Respond(At(2, 10, 10));
        good.Resolve(At(2, 12), SlaCalendar.AllTheTime, Utc);

        TicketSla late = Ticket(Low);
        late.Respond(At(5, 12));

        TicketSla stillOpen = Ticket(Low, At(30, 10));

        SlaMonthReport report = SlaReporting.Month([good, late, stillOpen], November, Utc, 100);

        report.Opened.ShouldBe(3);
        report.Resolved.ShouldBe(1);
        report.ResponseMeasured.ShouldBe(2);
        report.ResponseMet.ShouldBe(1);
        report.ResponseMetPercent.ShouldBe(50m);
        report.ResolutionMeasured.ShouldBe(1);
        report.ResolutionMetPercent.ShouldBe(100m);
        report.Breaches.ShouldBe(1);
        report.CreditPercent.ShouldBe(1m);
        report.Priorities.Select(line => line.Priority).ShouldBe([WorkPriority.Critical, WorkPriority.Low]);
    }

    [Fact]
    public void TheReport_WithNothingMeasured_SaysSoRatherThanAHundredPercent()
    {
        SlaMonthReport report = SlaReporting.Month([], November, Utc, 100);

        report.ResponseMetPercent.ShouldBeNull();
        report.ResolutionMetPercent.ShouldBeNull();
        report.Priorities.ShouldBeEmpty();
    }

    [Fact]
    public void AnExcludedBreach_CountsAsMet_AndIsReportedAsExcluded()
    {
        TicketSla excluded = Breached();
        excluded.Exclude("Client's own outage", null);

        SlaMonthReport report = SlaReporting.Month([excluded], November, Utc, 100);

        report.ResponseMet.ShouldBe(1);
        report.Excluded.ShouldBe(1);
        report.Breaches.ShouldBe(0);
    }

    [Fact]
    public void TheSpreadsheet_QuotesAndDefuses()
    {
        ServiceLevelRow row = new(Guid.NewGuid(), "CARE", "Support, L2", null, "=Acme", SlaReporting.Month([Breached()], November, Utc, 100));

        string[] lines = ServiceLevelCsv.Write([row]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[1].ShouldStartWith("'=Acme,\"CARE Support, L2\",2026-11,1,0,0.0,0.0,1,0,5");
    }
}
