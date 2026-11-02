import type { PunchEntry } from '@gggs/shared/models';

/**
 * Time worked today from the day's punches: each in-out pair, plus - while clocked in, an odd
 * number of punches - the stretch since the last punch-in, up to `now`.
 */
export function workedMs(punches: readonly PunchEntry[], now: number): number {
  let total = 0;
  for (let index = 0; index < punches.length; index += 2) {
    const start = instant(punches[index]!.at);
    const end = index + 1 < punches.length ? instant(punches[index + 1]!.at) : now;
    total += Math.max(0, end - start);
  }
  return total;
}

/**
 * A punch's moment. The API sends UTC; a timestamp without a zone suffix would otherwise be read as
 * the browser's local time - five and a half hours out in India.
 */
function instant(at: string): number {
  return Date.parse(/[zZ]|[+-]\d{2}:?\d{2}$/.test(at) ? at : `${at}Z`);
}

/** "07:05:09" - hours can run past 24 if someone forgets to clock out. */
export function clockLabel(ms: number): string {
  const seconds = Math.floor(ms / 1000);
  const pad = (value: number) => `${value}`.padStart(2, '0');
  return `${pad(Math.floor(seconds / 3600))}:${pad(Math.floor((seconds % 3600) / 60))}:${pad(seconds % 60)}`;
}
