/**
 * A moment's calendar date where the viewer is, "yyyy-MM-dd", for date fields and date filters.
 * `toISOString` would give the date in UTC, which in India is still yesterday until 05:30.
 */
export function localIsoDate(moment: Date = new Date()): string {
  return new Date(moment.getTime() - moment.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}
