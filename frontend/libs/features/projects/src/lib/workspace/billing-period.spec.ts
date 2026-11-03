import { describe, expect, it } from 'vitest';
import { monthBefore, monthBounds } from './billing-period';

describe('billing period', () => {
  it('finds the last day of a month, leap years included', () => {
    expect(monthBounds('2026-10')).toEqual({ from: '2026-10-01', to: '2026-10-31' });
    expect(monthBounds('2026-02')).toEqual({ from: '2026-02-01', to: '2026-02-28' });
    expect(monthBounds('2028-02')).toEqual({ from: '2028-02-01', to: '2028-02-29' });
  });

  it('steps back a month, across a new year', () => {
    expect(monthBefore('2026-11-03')).toBe('2026-10');
    expect(monthBefore('2026-10-31')).toBe('2026-09');
    expect(monthBefore('2027-01-15')).toBe('2026-12');
  });
});
