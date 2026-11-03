import type { PunchEntry } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { clockLabel, workedMs } from './work-time';

const punch = (at: string): PunchEntry => ({ at, localTime: at.slice(11, 19), source: 'Web' });

describe('workedMs', () => {
  const now = Date.parse('2026-10-30T13:30:00Z');

  it('is nothing before the first punch', () => {
    expect(workedMs([], now)).toBe(0);
  });

  it('runs from the punch-in while clocked in', () => {
    expect(workedMs([punch('2026-10-30T10:00:00Z')], now)).toBe(3.5 * 3600_000);
  });

  it('reads a timestamp without a zone as UTC, as the API means it', () => {
    expect(workedMs([punch('2026-10-30T13:00:00')], now)).toBe(30 * 60_000);
  });

  it('adds each in-out pair and stops at the punch-out', () => {
    const punches = [punch('2026-10-30T09:00:00Z'), punch('2026-10-30T11:00:00Z'), punch('2026-10-30T12:00:00Z'), punch('2026-10-30T13:00:00Z')];
    expect(workedMs(punches, now)).toBe(3 * 3600_000);
  });
});

describe('clockLabel', () => {
  it('shows hours, minutes and seconds', () => {
    expect(clockLabel(0)).toBe('00:00:00');
    expect(clockLabel((7 * 3600 + 5 * 60 + 9) * 1000)).toBe('07:05:09');
    expect(clockLabel(26 * 3600_000)).toBe('26:00:00');
  });
});
