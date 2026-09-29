using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Attendance;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class ShiftTests
{
    [Fact]
    public void Create_WithAHalfDayThresholdAtOrAboveFullDay_Throws()
    {
        Should.Throw<DomainRuleException>(() => Shift.Create(
            "DAY", "Day Shift", new TimeOnly(9, 0), new TimeOnly(18, 0), breakMinutes: 60, isNightShift: false,
            graceInMinutes: 10, graceOutMinutes: 10, fullDayMinMinutes: 480, halfDayMinMinutes: 480));
    }

    [Fact]
    public void Create_WithNegativeGraceMinutes_Throws()
    {
        Should.Throw<DomainRuleException>(() => Shift.Create(
            "DAY", "Day Shift", new TimeOnly(9, 0), new TimeOnly(18, 0), breakMinutes: 60, isNightShift: false,
            graceInMinutes: -1, graceOutMinutes: 10, fullDayMinMinutes: 480, halfDayMinMinutes: 240));
    }

    [Fact]
    public void ScheduledMinutes_ForADayShift_IsSpanMinusBreak()
    {
        Shift shift = Shift.Create(
            "DAY", "Day Shift", new TimeOnly(9, 0), new TimeOnly(18, 0), breakMinutes: 60, isNightShift: false,
            graceInMinutes: 10, graceOutMinutes: 10, fullDayMinMinutes: 480, halfDayMinMinutes: 240);

        // 09:00-18:00 = 540 minutes, minus a 60-minute break.
        shift.ScheduledMinutes().ShouldBe(480);
    }

    [Fact]
    public void ScheduledMinutes_ForANightShiftCrossingMidnight_AccountsForTheDayWrap()
    {
        Shift shift = Shift.Create(
            "NIGHT", "Night Shift", new TimeOnly(22, 0), new TimeOnly(6, 0), breakMinutes: 30, isNightShift: true,
            graceInMinutes: 10, graceOutMinutes: 10, fullDayMinMinutes: 420, halfDayMinMinutes: 210);

        // 22:00-06:00 = 480 minutes, minus a 30-minute break.
        shift.ScheduledMinutes().ShouldBe(450);
    }
}
