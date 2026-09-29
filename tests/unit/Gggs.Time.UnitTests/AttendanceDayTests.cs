using Gggs.Time.Domain.Attendance;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class AttendanceDayTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 6, 10);

    private static Shift DayShift(int graceInMinutes = 10, int graceOutMinutes = 10, int fullDayMinMinutes = 480, int halfDayMinMinutes = 240)
        => Shift.Create(
            "DAY", "Day Shift", new TimeOnly(9, 0), new TimeOnly(18, 0), breakMinutes: 60, isNightShift: false,
            graceInMinutes, graceOutMinutes, fullDayMinMinutes, halfDayMinMinutes);

    private static AttendancePunch Punch(TimeOnly time, PunchDirection direction)
        => AttendancePunch.Record(
            EmployeeId, Date.ToDateTime(time, DateTimeKind.Utc), direction, PunchSource.Web,
            deviceId: null, latitude: null, longitude: null, ipAddress: null, externalRef: null, isValid: true);

    [Fact]
    public void Recompute_WithApprovedLeave_TakesPrecedenceOverEverythingElse()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Guid leaveRequestId = Guid.NewGuid();

        day.Recompute(
            shiftId: null, shift: null, isWeeklyOff: true, isHoliday: true,
            approvedLeaveRequestId: leaveRequestId, validPunchesForDate: []);

        day.Status.ShouldBe(AttendanceDayStatus.OnLeave);
        day.LeaveRequestId.ShouldBe(leaveRequestId);
        day.WorkedMinutes.ShouldBe(0);
    }

    [Fact]
    public void Recompute_WithAHoliday_TakesPrecedenceOverWeeklyOffAndPunches()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift();

        day.Recompute(
            shift.Id, shift, isWeeklyOff: true, isHoliday: true,
            approvedLeaveRequestId: null, validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In)]);

        day.Status.ShouldBe(AttendanceDayStatus.Holiday);
    }

    [Fact]
    public void Recompute_WithNoLeaveOrHoliday_ButRosterMarksWeeklyOff_IsWeeklyOff()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);

        day.Recompute(shiftId: null, shift: null, isWeeklyOff: true, isHoliday: false, approvedLeaveRequestId: null, validPunchesForDate: []);

        day.Status.ShouldBe(AttendanceDayStatus.WeeklyOff);
    }

    [Fact]
    public void Recompute_WithNoPunches_OnAWorkingDay_IsAbsent()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift();

        day.Recompute(shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null, validPunchesForDate: []);

        day.Status.ShouldBe(AttendanceDayStatus.Absent);
        day.FirstIn.ShouldBeNull();
    }

    [Fact]
    public void Recompute_WithFullDayPunches_OnTime_IsPresentWithNoLateness()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift();

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In), Punch(new TimeOnly(18, 0), PunchDirection.Out)]);

        day.Status.ShouldBe(AttendanceDayStatus.Present);
        day.WorkedMinutes.ShouldBe(540);
        day.LateMinutes.ShouldBe(0);
        day.EarlyOutMinutes.ShouldBe(0);
    }

    [Fact]
    public void Recompute_ArrivingAfterGraceMinutes_RecordsLateMinutesPastTheGrace()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(graceInMinutes: 10);

        // 30 minutes after the 09:00 start, 10 of which are inside the grace window.
        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 30), PunchDirection.In), Punch(new TimeOnly(18, 0), PunchDirection.Out)]);

        day.LateMinutes.ShouldBe(20);
    }

    [Fact]
    public void Recompute_ArrivingWithinGraceMinutes_RecordsNoLateness()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(graceInMinutes: 10);

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 5), PunchDirection.In), Punch(new TimeOnly(18, 0), PunchDirection.Out)]);

        day.LateMinutes.ShouldBe(0);
    }

    [Fact]
    public void Recompute_LeavingBeforeGraceOutWindow_RecordsEarlyOutMinutes()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(graceOutMinutes: 10);

        // Leaves 30 minutes before the 18:00 end, 10 of which are inside the grace window.
        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In), Punch(new TimeOnly(17, 30), PunchDirection.Out)]);

        day.EarlyOutMinutes.ShouldBe(20);
    }

    [Fact]
    public void Recompute_WorkedMinutesAtOrAboveFullDayThreshold_IsPresent()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(fullDayMinMinutes: 480, halfDayMinMinutes: 240);

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In), Punch(new TimeOnly(18, 0), PunchDirection.Out)]);

        day.WorkedMinutes.ShouldBe(540);
        day.Status.ShouldBe(AttendanceDayStatus.Present);
    }

    [Fact]
    public void Recompute_WorkedMinutesBetweenHalfAndFullDayThresholds_IsHalfDay()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(fullDayMinMinutes: 480, halfDayMinMinutes: 240);

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In), Punch(new TimeOnly(14, 0), PunchDirection.Out)]);

        day.WorkedMinutes.ShouldBe(300);
        day.Status.ShouldBe(AttendanceDayStatus.HalfDay);
    }

    [Fact]
    public void Recompute_WorkedMinutesBelowHalfDayThreshold_IsAbsent()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift(fullDayMinMinutes: 480, halfDayMinMinutes: 240);

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In), Punch(new TimeOnly(10, 0), PunchDirection.Out)]);

        day.WorkedMinutes.ShouldBe(60);
        day.Status.ShouldBe(AttendanceDayStatus.Absent);
    }

    [Fact]
    public void Recompute_WithOnlyASinglePunch_RecordsFirstInButZeroWorkedMinutes()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        Shift shift = DayShift();

        day.Recompute(
            shift.Id, shift, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null,
            validPunchesForDate: [Punch(new TimeOnly(9, 0), PunchDirection.In)]);

        day.FirstIn.ShouldNotBeNull();
        day.WorkedMinutes.ShouldBe(0);
        day.Status.ShouldBe(AttendanceDayStatus.Absent);
    }

    [Fact]
    public void Recompute_IsRejected_WhenTheDayIsLocked()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);
        day.Lock();

        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() =>
            day.Recompute(shiftId: null, shift: null, isWeeklyOff: false, isHoliday: false, approvedLeaveRequestId: null, validPunchesForDate: []));
    }

    [Fact]
    public void ApplyRegularization_SetsPresentAndMarksRegularized()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);

        day.ApplyRegularization(
            Date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
            Date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc));

        day.Status.ShouldBe(AttendanceDayStatus.Present);
        day.IsRegularized.ShouldBeTrue();
        day.WorkedMinutes.ShouldBe(540);
    }

    [Fact]
    public void ApplyRegularization_WithOutBeforeIn_Throws()
    {
        AttendanceDay day = AttendanceDay.CreateEmpty(EmployeeId, Date);

        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() =>
            day.ApplyRegularization(
                Date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc),
                Date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)));
    }
}
