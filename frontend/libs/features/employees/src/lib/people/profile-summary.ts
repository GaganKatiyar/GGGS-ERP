/** A date-only ISO string (yyyy-mm-dd) as a local date, so no time zone moves it a day. */
function localDate(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number);
  return new Date(year!, month! - 1, day!);
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/** Whole months served since joining (0 before the first full month). */
export function monthsServed(joiningDate: string, today: Date): number {
  const joined = localDate(joiningDate);
  let months = (today.getFullYear() - joined.getFullYear()) * 12 + (today.getMonth() - joined.getMonth());
  if (today.getDate() < joined.getDate()) {
    months--;
  }
  return Math.max(0, months);
}

/** "5 yrs 7 mos", "1 yr", "3 mos" - short enough for a stat tile. */
export function tenureLabel(joiningDate: string, today: Date): string {
  const months = monthsServed(joiningDate, today);
  if (months === 0) {
    return localDate(joiningDate) > startOfDay(today) ? 'Not started' : 'New joiner';
  }

  const years = Math.floor(months / 12);
  const rest = months % 12;
  const parts = [];
  if (years) {
    parts.push(`${years} ${years === 1 ? 'yr' : 'yrs'}`);
  }
  if (rest) {
    parts.push(`${rest} ${rest === 1 ? 'mo' : 'mos'}`);
  }
  return parts.join(' ');
}

export interface Anniversary {
  readonly date: Date;
  /** Which anniversary it is: 1 for the first year completed. */
  readonly years: number;
  /** "today", "in 12 days", "in 4 months". */
  readonly when: string;
}

/** The next work anniversary on or after today. */
export function nextAnniversary(joiningDate: string, today: Date): Anniversary {
  const joined = localDate(joiningDate);
  const day = startOfDay(today);
  let years = Math.max(1, day.getFullYear() - joined.getFullYear());
  let date = new Date(joined.getFullYear() + years, joined.getMonth(), joined.getDate());
  if (date < day) {
    years++;
    date = new Date(joined.getFullYear() + years, joined.getMonth(), joined.getDate());
  }

  const days = Math.round((date.getTime() - day.getTime()) / 86_400_000);
  const months = Math.round(days / 30.44);
  const when = days === 0 ? 'today' : days < 45 ? `in ${days} ${days === 1 ? 'day' : 'days'}` : `in ${months} months`;
  return { date, years, when };
}

// Shared with the home dashboard's nudges, which cannot import this feature.
export { profileCompleteness, type ProfileCompleteness, type ProfileGap } from '@gggs/shared/employee-reference';

/** Up to two initials from a name, for an avatar without a photo. */
export function initialsOf(name: string | undefined): string {
  return (name ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}
