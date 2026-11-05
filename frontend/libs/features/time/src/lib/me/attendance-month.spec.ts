import type { AttendanceDay, AttendanceStatus, ScheduledDay } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { addMonths, calendarMonth, isFixable, monthRange, statusCounts } from './attendance-month';

function day(date: string, status: AttendanceStatus, isToday = false): AttendanceDay {
  return {
    employeeId: 'e',
    date,
    shiftName: 'General',
    scheduledStart: '10:00:00',
    scheduledEnd: '19:00:00',
    workedMinutes: 0,
    lateMinutes: 0,
    earlyLeaveMinutes: 0,
    status,
    punchCount: 0,
    workedOnDayOff: false,
    isRegularized: false,
    isToday,
  };
}

function shift(date: string, isWeeklyOff = false): ScheduledDay {
  return { employeeId: 'e', date, shiftName: 'General', start: '10:00:00', end: '19:00:00', breakMinutes: 60, graceMinutes: 15, halfDayMinutes: 240, fullDayMinutes: 480, isWeeklyOff, isOvernight: false };
}

describe('monthRange', () => {
  it('finds the last day of short and long months', () => {
    expect(monthRange('2026-11')).toEqual({ first: '2026-11-01', last: '2026-11-30' });
    expect(monthRange('2026-12')).toEqual({ first: '2026-12-01', last: '2026-12-31' });
    expect(monthRange('2026-02')).toEqual({ first: '2026-02-01', last: '2026-02-28' });
  });

  it('knows a leap year', () => {
    expect(monthRange('2028-02').last).toBe('2028-02-29');
  });
});

describe('addMonths', () => {
  it('moves within a year', () => {
    expect(addMonths('2026-11', -1)).toBe('2026-10');
    expect(addMonths('2026-10', 1)).toBe('2026-11');
  });

  it('crosses the year in both directions', () => {
    expect(addMonths('2027-01', -1)).toBe('2026-12');
    expect(addMonths('2026-12', 1)).toBe('2027-01');
    expect(addMonths('2026-11', -23)).toBe('2024-12');
  });
});

describe('calendarMonth', () => {
  it('starts the week on Monday', () => {
    // 1 Nov 2026 is a Sunday, the last column; 1 Oct 2026 is a Thursday; 1 Jun 2026 is a Monday.
    expect(calendarMonth('2026-11', [], [], '2026-11-05').leadingBlanks).toBe(6);
    expect(calendarMonth('2026-10', [], [], '2026-11-05').leadingBlanks).toBe(3);
    expect(calendarMonth('2026-06', [], [], '2026-11-05').leadingBlanks).toBe(0);
  });

  it('has one square for every day of the month, even with nothing recorded', () => {
    const { cells } = calendarMonth('2026-11', [], [], '2026-11-05');
    expect(cells).toHaveLength(30);
    expect(cells[0].date).toBe('2026-11-01');
    expect(cells[0].dayOfMonth).toBe(1);
    expect(cells[29].date).toBe('2026-11-30');
  });

  it('marks today and the days still to come', () => {
    const { cells } = calendarMonth('2026-11', [], [], '2026-11-05');
    expect(cells.filter((cell) => cell.isToday).map((cell) => cell.date)).toEqual(['2026-11-05']);
    expect(cells[3].isFuture).toBe(false);
    expect(cells[4].isFuture).toBe(false);
    expect(cells[5].isFuture).toBe(true);
  });

  it('has no future days in a past month', () => {
    expect(calendarMonth('2026-10', [], [], '2026-11-05').cells.some((cell) => cell.isFuture || cell.isToday)).toBe(false);
  });

  it('places each record and each scheduled shift on its own date', () => {
    const { cells } = calendarMonth('2026-11', [day('2026-11-03', 'Absent')], [shift('2026-11-09'), shift('2026-11-08', true)], '2026-11-05');
    expect(cells[2].recorded?.status).toBe('Absent');
    expect(cells[2].scheduled).toBeNull();
    expect(cells[8].scheduled?.isWeeklyOff).toBe(false);
    expect(cells[7].scheduled?.isWeeklyOff).toBe(true);
    expect(cells[8].recorded).toBeNull();
  });
});

describe('statusCounts', () => {
  it('counts in a fixed order and leaves out what did not occur', () => {
    const counts = statusCounts([day('2026-11-01', 'WeeklyOff'), day('2026-11-02', 'Present'), day('2026-11-03', 'Absent'), day('2026-11-04', 'Present')]);
    expect(counts).toEqual([
      { status: 'Present', count: 2 },
      { status: 'Absent', count: 1 },
      { status: 'WeeklyOff', count: 1 },
    ]);
  });

  it('is empty when nothing was recorded', () => {
    expect(statusCounts([])).toEqual([]);
  });
});

describe('isFixable', () => {
  it('offers a correction only for days that went wrong', () => {
    expect((['Absent', 'MissingPunch', 'HalfDay'] as const).every((status) => isFixable(day('2026-11-03', status)))).toBe(true);
    expect((['Present', 'WeeklyOff', 'Holiday', 'OnLeave'] as const).some((status) => isFixable(day('2026-11-03', status)))).toBe(false);
  });

  it('never offers one for today, which is still going on', () => {
    expect(isFixable(day('2026-11-05', 'MissingPunch', true))).toBe(false);
  });
});
