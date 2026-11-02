import type { EducationEntry, WorkExperienceEntry } from '@gggs/shared/models';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2012 – 2016", "2012 – present"-style years, or nothing when neither is known. */
export function educationYears(entry: Pick<EducationEntry, 'startYear' | 'endYear'>): string {
  if (entry.startYear && entry.endYear) {
    return entry.startYear === entry.endYear ? `${entry.endYear}` : `${entry.startYear} – ${entry.endYear}`;
  }
  if (entry.endYear) {
    return `Finished ${entry.endYear}`;
  }
  return entry.startYear ? `Started ${entry.startYear}` : '';
}

/** "Jul 2016" from an ISO date, read as written: no time zone can move it to another month. */
export function monthLabel(isoDate: string): string {
  const [year, month] = isoDate.split('-');
  return `${MONTHS[Number(month) - 1] ?? ''} ${year}`;
}

/** Whole months from the start month to the end month, both counted: Jul 2016 – Mar 2019 is 33. */
export function monthsSpanned(fromDate: string, toDate: string): number {
  const [fromYear, fromMonth] = fromDate.split('-').map(Number);
  const [toYear, toMonth] = toDate.split('-').map(Number);
  return Math.max(0, ((toYear ?? 0) - (fromYear ?? 0)) * 12 + ((toMonth ?? 0) - (fromMonth ?? 0)) + 1);
}

/** "2 yrs 9 mos", "1 yr", "4 mos". */
export function durationLabel(months: number): string {
  const years = Math.floor(months / 12);
  const rest = months % 12;
  const parts = [years ? `${years} ${years === 1 ? 'yr' : 'yrs'}` : '', rest ? `${rest} ${rest === 1 ? 'mo' : 'mos'}` : ''].filter(Boolean);
  return parts.join(' ') || 'Under a month';
}

/** A job's span, "Jul 2016 – Mar 2019 · 2 yrs 9 mos"; an open one runs to today. */
export function experienceSpan(entry: Pick<WorkExperienceEntry, 'fromDate' | 'toDate'>, today: string): string {
  const end = entry.toDate ?? today;
  return `${monthLabel(entry.fromDate)} – ${entry.toDate ? monthLabel(entry.toDate) : 'Present'} · ${durationLabel(monthsSpanned(entry.fromDate, end))}`;
}

/** Experience before joining, summed: overlapping jobs are counted once. */
export function totalExperience(entries: readonly Pick<WorkExperienceEntry, 'fromDate' | 'toDate'>[], today: string): number {
  const months = new Set<number>();
  for (const entry of entries) {
    const [fromYear, fromMonth] = entry.fromDate.split('-').map(Number);
    const span = monthsSpanned(entry.fromDate, entry.toDate ?? today);
    const first = (fromYear ?? 0) * 12 + (fromMonth ?? 1) - 1;
    for (let index = 0; index < span; index++) {
      months.add(first + index);
    }
  }
  return months.size;
}

/** The value a month input shows for an ISO date, and back: the first of the month. */
export const toMonthInput = (isoDate: string | undefined): string | null => (isoDate ? isoDate.slice(0, 7) : null);
export const fromMonthInput = (month: string | null | undefined): string | undefined => (month ? `${month}-01` : undefined);
