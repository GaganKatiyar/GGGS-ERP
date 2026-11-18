import { formatDate } from '@angular/common';

/**
 * A moment's calendar date where the viewer is, "yyyy-MM-dd", for date fields and date filters.
 * `toISOString` would give the date in UTC, which in India is still yesterday until 05:30.
 */
export function localIsoDate(moment: Date = new Date()): string {
  return new Date(moment.getTime() - moment.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

// ----- Showing times: one 12-hour style everywhere, "1:50 PM" ------------------------------------
// Every format goes through Angular's formatDate with a fixed locale, never the browser's own, so a
// PC set to en-GB does not turn the app back to 24-hour and one set to en-IN does not print "pm".

/** The app's locale for dates and times; Angular's default, whose "a" prints AM and PM. */
const LOCALE = 'en-US';

/** Date-pipe formats: `{{ value | date: DATE_TIME }}`. */
export const TIME = 'h:mm a';
export const DATE_TIME = 'd MMM y, h:mm a';
export const SHORT_DATE_TIME = 'd MMM, h:mm a';

const TIME_OF_DAY = /^(\d{1,2}):(\d{2})/;
const ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;

/** "1:05 PM" from a time of day as the API sends it ("13:05:00" or "13:05"); empty for nothing. */
export function clockTime(time: string | null | undefined): string {
  const match = time ? TIME_OF_DAY.exec(time) : null;
  if (!match) {
    return '';
  }

  const hours = Number(match[1]);
  return `${hours % 12 || 12}:${match[2]} ${hours < 12 ? 'AM' : 'PM'}`;
}

/** "9:30 AM – 6:30 PM"; one end alone when the other is missing. */
export function timeRange(start: string | null | undefined, end: string | null | undefined): string {
  return [clockTime(start), clockTime(end)].filter(Boolean).join(' – ');
}

/**
 * "17 Nov 2026, 1:50 PM" in the viewer's time zone. An API timestamp without a zone is UTC, as the
 * server stores it; a plain date stays a date. Anything unreadable comes back as given.
 */
export function dateTime(value: string | Date | null | undefined, format: string = DATE_TIME): string {
  if (!value) {
    return '';
  }

  if (value instanceof Date) {
    return formatDate(value, format, LOCALE);
  }

  if (ISO_DATE_TIME.test(value)) {
    const utc = /[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`;
    return formatDate(utc, format, LOCALE);
  }

  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? formatDate(`${value}T00:00:00`, 'd MMM y', LOCALE) : value;
}

/** Only the time of a moment: "1:50 PM". */
export function momentTime(value: string | Date | null | undefined): string {
  return dateTime(value, TIME);
}
