using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Shift.Domain;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class LeaveCalculatorTests
{
    private static readonly DateOnly Friday = new(2026, 10, 16);

    /// <summary>Friday to Monday with the weekend off.</summary>
    private static List<LeaveCalendarDay> FridayToMonday() =>
    [
        new(Friday, true),
        new(Friday.AddDays(1), false),
        new(Friday.AddDays(2), false),
        new(Friday.AddDays(3), true),
    ];

    [Fact]
    public void WorkingDaysCount_WeekendsDoNot()
        => LeaveCalculator.CountDays(FridayToMonday(), false, false, countSandwichedDays: false).ShouldBe(2m);

    [Fact]
    public void TheSandwichRule_CountsOffDaysBetweenLeaveDays()
        => LeaveCalculator.CountDays(FridayToMonday(), false, false, countSandwichedDays: true).ShouldBe(4m);

    [Fact]
    public void TheSandwichRule_DoesNotCountOffDaysAtTheEdges()
    {
        List<LeaveCalendarDay> saturdayToMonday = [new(Friday.AddDays(1), false), new(Friday.AddDays(2), false), new(Friday.AddDays(3), true)];
        LeaveCalculator.CountDays(saturdayToMonday, false, false, countSandwichedDays: true).ShouldBe(1m);
    }

    [Fact]
    public void HalfDaysTakeHalf()
    {
        LeaveCalculator.CountDays(FridayToMonday(), startsSecondHalf: true, endsFirstHalf: true, countSandwichedDays: false).ShouldBe(1m);
        LeaveCalculator.CountDays([new(Friday, true)], startsSecondHalf: true, endsFirstHalf: false, countSandwichedDays: false).ShouldBe(0.5m);
    }

    [Fact]
    public void ASingleDayCannotBeBothHalves()
        => Should.Throw<DomainRuleException>(() => LeaveCalculator.CountDays([new(Friday, true)], true, true, false)).ErrorCode.ShouldBe("LEAVE_HALF_DAYS");

    [Fact]
    public void OnlyOffDays_TakeNothing()
        => LeaveCalculator.CountDays([new(Friday.AddDays(1), false), new(Friday.AddDays(2), false)], false, false, true).ShouldBe(0m);
}

public sealed class AttendanceEvaluatorTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly Guid Someone = Guid.NewGuid();

    private static DaySchedule Office(bool weeklyOff = false)
        => new("Day shift", Day.ToDateTime(new TimeOnly(9, 0)), Day.ToDateTime(new TimeOnly(18, 0)), 60, 15, 240, 480, weeklyOff);

    private static LocalPunch At(int hour, int minute, int dayOffset = 0)
    {
        DateTime local = Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute));
        return new LocalPunch(local, local.AddHours(-5.5), false);
    }

    [Fact]
    public void AFullDay_IsPresent_AndLateOnlyBeyondGrace()
    {
        DayEvaluation onTime = AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 10), At(18, 15)], false, false, null);
        onTime.Status.ShouldBe(AttendanceStatus.Present);
        onTime.LateMinutes.ShouldBe(0);
        onTime.WorkedMinutes.ShouldBe(485, "nine hours five minutes less the hour's break");

        DayEvaluation late = AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 40), At(18, 45)], false, false, null);
        late.LateMinutes.ShouldBe(25);
        late.EarlyLeaveMinutes.ShouldBe(0);
    }

    [Fact]
    public void ShortDays_AreHalfDaysOrAbsences()
    {
        AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 0), At(14, 30)], false, false, null).Status.ShouldBe(AttendanceStatus.HalfDay);
        AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 0), At(11, 0)], false, false, null).Status.ShouldBe(AttendanceStatus.Absent);
    }

    [Fact]
    public void HalfDayLeave_HalvesTheThresholds()
        => AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 0), At(14, 0)], false, halfDayLeave: true, null).Status.ShouldBe(AttendanceStatus.Present);

    [Fact]
    public void LeaveHolidaysAndOffDays_ComeBeforePunches()
    {
        AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 0), At(18, 0)], fullDayLeave: true, false, null).Status.ShouldBe(AttendanceStatus.OnLeave);
        AttendanceEvaluator.Evaluate(Someone, Day, Office(), [], false, false, "Diwali").Status.ShouldBe(AttendanceStatus.Holiday);
        AttendanceEvaluator.Evaluate(Someone, Day, Office(weeklyOff: true), [], false, false, null).Status.ShouldBe(AttendanceStatus.WeeklyOff);
    }

    [Fact]
    public void WorkOnADayOff_IsCountedAndFlagged()
    {
        DayEvaluation evaluation = AttendanceEvaluator.Evaluate(Someone, Day, Office(weeklyOff: true), [At(10, 0), At(19, 0)], false, false, null);
        evaluation.Status.ShouldBe(AttendanceStatus.Present);
        evaluation.WorkedOnDayOff.ShouldBeTrue();
        evaluation.LateMinutes.ShouldBe(0, "nobody is late on a day off");
    }

    [Fact]
    public void OnePunch_IsAMissingPunch()
        => AttendanceEvaluator.Evaluate(Someone, Day, Office(), [At(9, 0)], false, false, null).Status.ShouldBe(AttendanceStatus.MissingPunch);

    [Fact]
    public void ANightShift_IsJudgedAcrossMidnight()
    {
        var night = new DaySchedule("Night", Day.ToDateTime(new TimeOnly(22, 0)), Day.AddDays(1).ToDateTime(new TimeOnly(7, 0)), 60, 15, 240, 420, false);
        DayEvaluation evaluation = AttendanceEvaluator.Evaluate(Someone, Day, night, [At(22, 0), At(7, 0, dayOffset: 1)], false, false, null);
        evaluation.Status.ShouldBe(AttendanceStatus.Present);
        evaluation.WorkedMinutes.ShouldBe(480);
    }
}

public sealed class ShiftRuleTests
{
    [Fact]
    public void ANightShift_LastsAcrossMidnight()
        => WorkShift.Create("N", "Night", new TimeOnly(22, 0), new TimeOnly(6, 0), 30, 10, 200, 420, [DayOfWeek.Sunday]).DurationMinutes.ShouldBe(480);

    [Fact]
    public void WeeklyOffs_RoundTripThroughTheMask()
    {
        WorkShift shift = WorkShift.Create("D", "Day", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 15, 240, 480, [DayOfWeek.Saturday, DayOfWeek.Sunday]);
        shift.IsOff(DayOfWeek.Sunday).ShouldBeTrue();
        shift.IsOff(DayOfWeek.Monday).ShouldBeFalse();
        shift.WeeklyOffDays.ShouldBe([DayOfWeek.Sunday, DayOfWeek.Saturday]);
    }

    [Fact]
    public void Thresholds_MustFitTheShift()
    {
        Should.Throw<DomainRuleException>(() => WorkShift.Create("X", "X", new TimeOnly(9, 0), new TimeOnly(13, 0), 30, 10, 200, 480, [])).ErrorCode.ShouldBe("SHIFT_THRESHOLDS");
        Should.Throw<DomainRuleException>(() => WorkShift.Create("X", "X", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 15, 240, 480, Enum.GetValues<DayOfWeek>())).ErrorCode.ShouldBe("SHIFT_NO_WORKING_DAY");
    }

    [Fact]
    public void AnAssignment_KnowsWhichDaysItCovers()
    {
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        assignment.Covers(new DateOnly(2026, 1, 15)).ShouldBeTrue();
        assignment.Covers(new DateOnly(2026, 2, 1)).ShouldBeFalse();
        assignment.Overlaps(new DateOnly(2026, 1, 31), null).ShouldBeTrue();
        assignment.Overlaps(new DateOnly(2026, 2, 1), null).ShouldBeFalse();
    }
}
