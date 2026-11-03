/** The first and last day of a month written as yyyy-MM, as a month input gives it. */
export function monthBounds(month: string): { readonly from: string; readonly to: string } {
  const [year, number] = month.split('-').map(Number);
  const lastDay = new Date(year, number, 0).getDate();
  return { from: `${month}-01`, to: `${month}-${String(lastDay).padStart(2, '0')}` };
}

/** The month before the given day, as yyyy-MM: invoices usually go out for the month just finished. */
export function monthBefore(day: string): string {
  const [year, number] = day.split('-').map(Number);
  return number === 1 ? `${year - 1}-12` : `${year}-${String(number - 1).padStart(2, '0')}`;
}
