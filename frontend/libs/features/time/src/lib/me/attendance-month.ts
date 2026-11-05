import type { AttendanceDay, AttendanceStatus, ScheduledDay } from '@gggs/shared/models';

/** Whether a day can be corrected. Today is still going on, so it is never offered, whatever its status. */
const FIXABLE: ReadonlySet<AttendanceStatus> = new Set<AttendanceStatus>(['Absent', 'MissingPunch', 'HalfDay']);

/** The order statuses are counted in above the days: what happened, then what was excused or planned. */
const COUNT_ORDER: readonly AttendanceStatus[] = ['Present', 'Absent', 'MissingPunch', 'HalfDay', 'OnLeave', 'Holiday', 'WeeklyOff'];

/** One square on the calendar: what happened that day, or for a day still to come, what is scheduled. */
export interface CalendarCell {
  readonly date: string;
  /** The day of the month, 1 to 31. */
  readonly dayOfMonth: number;
  readonly isToday: boolean;
  readonly isFuture: boolean;
  readonly recorded: AttendanceDay | null;
  readonly scheduled: ScheduledDay | null;
}

export interface CalendarMonth {
  /** Empty squares before the 1st, in a week that starts on Monday. */
  readonly leadingBlanks: number;
  readonly cells: readonly CalendarCell[];
}

export interface StatusCount {
  readonly status: AttendanceStatus;
  readonly count: number;
}

export function isFixable(day: AttendanceDay): boolean {
  return !day.isToday && FIXABLE.has(day.status);
}

/** The first and last dates of a month given as `yyyy-MM`. */
export function monthRange(month: string): { readonly first: string; readonly last: string } {
  const [year, monthNumber] = month.split('-').map(Number);
  return { first: `${month}-01`, last: `${month}-${String(daysIn(year, monthNumber)).padStart(2, '0')}` };
}

/** The month `by` months away, as `yyyy-MM`. */
export function addMonths(month: string, by: number): string {
  const [year, monthNumber] = month.split('-').map(Number);
  const total = year * 12 + (monthNumber - 1) + by;
  return `${Math.floor(total / 12)}-${String((total % 12) + 1).padStart(2, '0')}`;
}

/** Lays a month out for the calendar. Dates are compared as text, so no time zone can move a day. */
export function calendarMonth(month: string, days: readonly AttendanceDay[], scheduled: readonly ScheduledDay[], today: string): CalendarMonth {
  const [year, monthNumber] = month.split('-').map(Number);
  const recordedByDate = new Map(days.map((day) => [day.date, day]));
  const scheduledByDate = new Map(scheduled.map((day) => [day.date, day]));

  const cells = Array.from({ length: daysIn(year, monthNumber) }, (_, index): CalendarCell => {
    const dayOfMonth = index + 1;
    const date = `${month}-${String(dayOfMonth).padStart(2, '0')}`;
    return {
      date,
      dayOfMonth,
      isToday: date === today,
      isFuture: date > today,
      recorded: recordedByDate.get(date) ?? null,
      scheduled: scheduledByDate.get(date) ?? null,
    };
  });

  // getUTCDay gives 0 for Sunday; shifted so Monday comes first.
  return { leadingBlanks: (new Date(Date.UTC(year, monthNumber - 1, 1)).getUTCDay() + 6) % 7, cells };
}

/** How many days had each status, leaving out statuses that did not occur. */
export function statusCounts(days: readonly AttendanceDay[]): readonly StatusCount[] {
  return COUNT_ORDER.map((status) => ({ status, count: days.filter((day) => day.status === status).length })).filter((entry) => entry.count > 0);
}

function daysIn(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}
