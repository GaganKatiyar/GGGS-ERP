import type { SlaTicket } from '@gggs/shared/models';

/** Targets are held in minutes and typed in hours. */
export const minutesToHours = (minutes: number): number => Math.round((minutes / 60) * 100) / 100;

export const hoursToMinutes = (hours: number): number => Math.round(hours * 60);

/** How long until a moment, or how long past it, in the fewest words that are still useful. */
export function timeLeft(dueIso: string, nowMs: number): string {
  const minutes = Math.round((new Date(dueIso).getTime() - nowMs) / 60000);
  const size = Math.abs(minutes);
  const amount = size < 60 ? `${size} min` : size < 48 * 60 ? `${Math.round(size / 60)} h` : `${Math.round(size / (24 * 60))} days`;
  return minutes < 0 ? `${amount} over` : `${amount} left`;
}

export type TargetTone = 'success' | 'warning' | 'danger' | 'neutral';

export interface TargetState {
  readonly label: string;
  readonly tone: TargetTone;
}

/** Where a ticket stands on its response target. */
export function responseState(ticket: SlaTicket, nowMs: number): TargetState {
  if (ticket.responseBreached) {
    return { label: 'Missed', tone: 'danger' };
  }

  if (ticket.respondedAt) {
    return { label: 'Met', tone: 'success' };
  }

  return running(ticket.responseDueAt, nowMs);
}

/** Where a ticket stands on its resolution target. A breach stays a breach, even once it is resolved. */
export function resolutionState(ticket: SlaTicket, nowMs: number): TargetState {
  if (ticket.resolutionBreached) {
    return { label: 'Missed', tone: 'danger' };
  }

  if (ticket.resolvedAt) {
    return { label: 'Met', tone: 'success' };
  }

  if (ticket.isPaused) {
    return { label: 'Paused', tone: 'neutral' };
  }

  return running(ticket.resolutionDueAt, nowMs);
}

function running(dueIso: string, nowMs: number): TargetState {
  const left = new Date(dueIso).getTime() - nowMs;
  return { label: timeLeft(dueIso, nowMs), tone: left < 0 ? 'danger' : left < 60 * 60000 ? 'warning' : 'neutral' };
}
