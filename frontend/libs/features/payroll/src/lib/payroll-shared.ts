import { ApiError } from '@gggs/core/http';

export const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const pad = (value: number) => String(value).padStart(2, '0');

/** The first day of the month an ISO date falls in. */
export const monthOf = (iso: string): string => `${iso.slice(0, 7)}-01`;

/** The first day of the current month. */
export const thisMonth = (): string => {
  const now = new Date();
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-01`;
};

/** The financial year (April-March) a date falls in, by the year it starts. */
export const financialYearOf = (iso: string): number => {
  const [year, month] = iso.split('-').map(Number);
  return month >= 4 ? year : year - 1;
};

export const financialYearLabel = (year: number): string => `FY ${year}-${String((year + 1) % 100).padStart(2, '0')}`;

/** The financial years offered in pickers: this one and the four before it. */
export const recentFinancialYears = (): readonly number[] => {
  const current = financialYearOf(thisMonth());
  return [current, current - 1, current - 2, current - 3, current - 4];
};
