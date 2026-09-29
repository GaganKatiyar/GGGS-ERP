using Gggs.Time.Application.Holidays;
using Gggs.Time.Domain.Holidays;
using Gggs.Time.Domain.Leave;
using NSubstitute;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class WorkCalendarTests
{
    [Fact]
    public async Task GetWorkDaysAsync_ForAFullMultiDayRange_ReturnsEveryDateAtFullFraction()
    {
        IHolidayRepository holidays = Substitute.For<IHolidayRepository>();
        holidays.GetForYearAsync(2026, Arg.Any<CancellationToken>()).Returns((HolidayCalendar?)null);

        WorkCalendar calendar = new(holidays);

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> days = await calendar.GetWorkDaysAsync(
            Guid.NewGuid(), new DateOnly(2026, 3, 2), LeaveDaySession.Full, new DateOnly(2026, 3, 4), LeaveDaySession.Full,
            TestContext.Current.CancellationToken);

        days.Count.ShouldBe(3);
        days.ShouldAllBe(d => d.DayFraction == 1m);
    }

    [Fact]
    public async Task GetWorkDaysAsync_WithAHalfDayStart_GivesTheFirstDateHalfFraction()
    {
        IHolidayRepository holidays = Substitute.For<IHolidayRepository>();
        holidays.GetForYearAsync(2026, Arg.Any<CancellationToken>()).Returns((HolidayCalendar?)null);

        WorkCalendar calendar = new(holidays);

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> days = await calendar.GetWorkDaysAsync(
            Guid.NewGuid(), new DateOnly(2026, 3, 2), LeaveDaySession.Second, new DateOnly(2026, 3, 3), LeaveDaySession.Full,
            TestContext.Current.CancellationToken);

        days.Count.ShouldBe(2);
        days[0].DayFraction.ShouldBe(0.5m);
        days[1].DayFraction.ShouldBe(1m);
    }

    [Fact]
    public async Task GetWorkDaysAsync_ForASingleHalfDayDate_ReturnsOneEntryAtHalfFraction()
    {
        IHolidayRepository holidays = Substitute.For<IHolidayRepository>();
        holidays.GetForYearAsync(2026, Arg.Any<CancellationToken>()).Returns((HolidayCalendar?)null);

        WorkCalendar calendar = new(holidays);

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> days = await calendar.GetWorkDaysAsync(
            Guid.NewGuid(), new DateOnly(2026, 3, 2), LeaveDaySession.First, new DateOnly(2026, 3, 2), LeaveDaySession.First,
            TestContext.Current.CancellationToken);

        days.Count.ShouldBe(1);
        days[0].DayFraction.ShouldBe(0.5m);
    }

    [Fact]
    public async Task GetWorkDaysAsync_SkipsANonOptionalHoliday()
    {
        HolidayCalendar holidayCalendar = HolidayCalendar.Create("2026", 2026, "IN", null);

        IHolidayRepository holidays = Substitute.For<IHolidayRepository>();
        holidays.GetForYearAsync(2026, Arg.Any<CancellationToken>()).Returns(holidayCalendar);
        holidays.GetHolidaysAsync(holidayCalendar.Id, Arg.Any<CancellationToken>())
            .Returns([Holiday.Create(holidayCalendar.Id, new DateOnly(2026, 3, 3), "Holi", isOptional: false, isRestricted: false)]);

        WorkCalendar calendar = new(holidays);

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> days = await calendar.GetWorkDaysAsync(
            Guid.NewGuid(), new DateOnly(2026, 3, 2), LeaveDaySession.Full, new DateOnly(2026, 3, 4), LeaveDaySession.Full,
            TestContext.Current.CancellationToken);

        days.Count.ShouldBe(2);
        days.ShouldNotContain(d => d.Date == new DateOnly(2026, 3, 3));
    }

    [Fact]
    public async Task GetWorkDaysAsync_DoesNotSkipAnOptionalHoliday()
    {
        HolidayCalendar holidayCalendar = HolidayCalendar.Create("2026", 2026, "IN", null);

        IHolidayRepository holidays = Substitute.For<IHolidayRepository>();
        holidays.GetForYearAsync(2026, Arg.Any<CancellationToken>()).Returns(holidayCalendar);
        holidays.GetHolidaysAsync(holidayCalendar.Id, Arg.Any<CancellationToken>())
            .Returns([Holiday.Create(holidayCalendar.Id, new DateOnly(2026, 3, 3), "Floating holiday", isOptional: true, isRestricted: false)]);

        WorkCalendar calendar = new(holidays);

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> days = await calendar.GetWorkDaysAsync(
            Guid.NewGuid(), new DateOnly(2026, 3, 2), LeaveDaySession.Full, new DateOnly(2026, 3, 4), LeaveDaySession.Full,
            TestContext.Current.CancellationToken);

        days.Count.ShouldBe(3);
    }
}
