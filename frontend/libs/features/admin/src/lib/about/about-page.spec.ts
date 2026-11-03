import { describe, expect, it } from 'vitest';
import { formatUptime } from './about-page';

describe('formatUptime', () => {
  const start = '2026-11-01T08:00:00Z';

  it('shows minutes under an hour', () => {
    expect(formatUptime(start, '2026-11-01T08:05:40Z')).toBe('5 min');
  });

  it('shows hours and minutes under a day', () => {
    expect(formatUptime(start, '2026-11-01T10:15:00Z')).toBe('2 h 15 min');
  });

  it('shows days and hours from a day on', () => {
    expect(formatUptime(start, '2026-11-04T12:30:00Z')).toBe('3 d 4 h');
  });

  it('never goes negative when the clocks disagree', () => {
    expect(formatUptime(start, '2026-11-01T07:59:00Z')).toBe('0 min');
  });
});
