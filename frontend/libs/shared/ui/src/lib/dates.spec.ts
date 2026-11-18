import { describe, expect, it } from 'vitest';
import { clockTime, dateTime, momentTime, timeRange } from './dates';

describe('12-hour times', () => {
  it('reads a time of day as the API sends it', () => {
    expect(clockTime('13:05:00')).toBe('1:05 PM');
    expect(clockTime('09:30')).toBe('9:30 AM');
    expect(clockTime('00:30')).toBe('12:30 AM');
    expect(clockTime('12:00:00')).toBe('12:00 PM');
    expect(clockTime('23:59')).toBe('11:59 PM');
    expect(clockTime(undefined)).toBe('');
    expect(clockTime('soon')).toBe('');
  });

  it('shows a shift as a range, or one end when the other is missing', () => {
    expect(timeRange('09:30:00', '18:30:00')).toBe('9:30 AM – 6:30 PM');
    expect(timeRange('09:30', null)).toBe('9:30 AM');
    expect(timeRange(null, null)).toBe('');
  });

  it('shows a moment in the viewer’s zone with AM or PM, reading a zoneless timestamp as UTC', () => {
    const moment = new Date(Date.UTC(2026, 10, 17, 8, 20));
    const local = `${moment.getHours() % 12 || 12}:${String(moment.getMinutes()).padStart(2, '0')} ${moment.getHours() < 12 ? 'AM' : 'PM'}`;

    expect(dateTime('2026-11-17T08:20:00Z')).toContain(local);
    expect(dateTime('2026-11-17T08:20:00')).toBe(dateTime('2026-11-17T08:20:00Z'));
    expect(momentTime(moment)).toBe(local);
    expect(dateTime('2026-11-17')).toBe('17 Nov 2026');
    expect(dateTime(null)).toBe('');
  });
});
