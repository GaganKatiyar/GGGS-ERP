import { describe, expect, it } from 'vitest';
import { emailStatusLabel, senderStatus, totalOf } from './email-model';

describe('email senders', () => {
  const row = { key: 'reward', hasOwn: false, isPaused: false, problem: undefined };

  it('says whether a module has its own sender or uses the default', () => {
    expect(senderStatus(row)).toBe('Uses default');
    expect(senderStatus({ ...row, hasOwn: true })).toBe('Own sender');
    expect(senderStatus({ ...row, key: 'default' })).toBe('Own sender');
  });

  it('puts a problem above a pause, and a pause above everything else', () => {
    expect(senderStatus({ ...row, hasOwn: true, isPaused: true })).toBe('Paused');
    expect(senderStatus({ ...row, hasOwn: true, isPaused: true, problem: 'Its connection was deleted.' })).toBe('Needs attention');
  });
});

describe('email log', () => {
  it('calls a logged email what it is', () => {
    expect(emailStatusLabel('Logged')).toBe('Logged only');
    expect(emailStatusLabel('Queued')).toBe('Waiting');
    expect(emailStatusLabel('Sent')).toBe('Sent');
  });

  it('counts every status for "All"', () => {
    expect(totalOf({ Sent: 3, Logged: 2, Failed: 1 })).toBe(6);
    expect(totalOf({})).toBe(0);
  });
});
