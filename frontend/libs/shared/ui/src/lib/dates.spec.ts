import { describe, expect, it, vi } from 'vitest';
import { localIsoDate } from './dates';

const at = (utc: string, offsetMinutes: number): Date => {
  const moment = new Date(utc);
  vi.spyOn(moment, 'getTimezoneOffset').mockReturnValue(offsetMinutes);
  return moment;
};

describe('localIsoDate', () => {
  it('is already tomorrow in India while UTC is still on today', () => {
    expect(localIsoDate(at('2026-11-01T20:00:00Z', -330))).toBe('2026-11-02');
    expect(localIsoDate(at('2026-11-01T18:29:00Z', -330))).toBe('2026-11-01');
    expect(localIsoDate(at('2026-11-01T18:30:00Z', -330))).toBe('2026-11-02');
  });

  it('is still yesterday west of Greenwich', () => {
    expect(localIsoDate(at('2026-11-02T02:00:00Z', 300))).toBe('2026-11-01');
  });

  it('crosses a month and a year', () => {
    expect(localIsoDate(at('2026-12-31T20:00:00Z', -330))).toBe('2027-01-01');
  });

  it('defaults to now', () => {
    expect(localIsoDate()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
