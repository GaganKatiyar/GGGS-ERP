import type { SlaTicket } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { hoursToMinutes, minutesToHours, resolutionState, responseState, timeLeft } from './service-level-text';

const now = new Date('2026-11-03T10:00:00Z').getTime();
const ticket = (changes: Partial<SlaTicket> = {}): SlaTicket => ({
  taskId: 't1',
  code: 'CARE-1',
  title: 'Payments are down',
  type: 'Incident',
  priority: 'Critical',
  openedAt: '2026-11-03T09:00:00Z',
  responseDueAt: '2026-11-03T10:30:00Z',
  resolutionDueAt: '2026-11-03T14:00:00Z',
  isPaused: false,
  responseBreached: false,
  resolutionBreached: false,
  ...changes,
});

describe('service level text', () => {
  it('turns hours into minutes and back', () => {
    expect(hoursToMinutes(0.5)).toBe(30);
    expect(minutesToHours(30)).toBe(0.5);
    expect(minutesToHours(hoursToMinutes(1.25))).toBe(1.25);
  });

  it('says how long is left, in a sensible unit', () => {
    expect(timeLeft('2026-11-03T10:30:00Z', now)).toBe('30 min left');
    expect(timeLeft('2026-11-03T14:00:00Z', now)).toBe('4 h left');
    expect(timeLeft('2026-11-08T10:00:00Z', now)).toBe('5 days left');
    expect(timeLeft('2026-11-03T09:00:00Z', now)).toBe('1 h over');
  });

  it('warns in the last hour, and shows time running out', () => {
    expect(responseState(ticket(), now)).toEqual({ label: '30 min left', tone: 'warning' });
    expect(resolutionState(ticket(), now)).toEqual({ label: '4 h left', tone: 'neutral' });
    expect(responseState(ticket({ responseDueAt: '2026-11-03T09:30:00Z' }), now).tone).toBe('danger');
  });

  it('settles once answered or resolved', () => {
    expect(responseState(ticket({ respondedAt: '2026-11-03T09:10:00Z' }), now)).toEqual({ label: 'Met', tone: 'success' });
    expect(resolutionState(ticket({ resolvedAt: '2026-11-03T09:50:00Z' }), now)).toEqual({ label: 'Met', tone: 'success' });
  });

  it('keeps a breach, even after the ticket is resolved', () => {
    expect(responseState(ticket({ respondedAt: '2026-11-03T09:50:00Z', responseBreached: true }), now).label).toBe('Missed');
    expect(resolutionState(ticket({ resolvedAt: '2026-11-03T09:50:00Z', resolutionBreached: true }), now).label).toBe('Missed');
  });

  it('shows a paused clock as paused, but a breach before the pause as missed', () => {
    expect(resolutionState(ticket({ isPaused: true }), now)).toEqual({ label: 'Paused', tone: 'neutral' });
    expect(resolutionState(ticket({ isPaused: true, resolutionBreached: true }), now).label).toBe('Missed');
  });
});
