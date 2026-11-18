import type { EmailSenderItem, EmailStatus } from '@gggs/shared/models';

export type SenderStatus = 'Own sender' | 'Uses default' | 'Paused' | 'Needs attention';

/** What the Email senders list says about a sender. A problem outranks everything: mail is not going the way it says. */
export function senderStatus(row: Pick<EmailSenderItem, 'key' | 'hasOwn' | 'isPaused' | 'problem'>): SenderStatus {
  if (row.problem) {
    return 'Needs attention';
  }
  if (row.isPaused) {
    return 'Paused';
  }
  return row.key === 'default' || row.hasOwn ? 'Own sender' : 'Uses default';
}

/** The Email log's status filter, in the order it shows them, with the words people see. */
export const EMAIL_STATUSES: readonly { readonly status: EmailStatus; readonly label: string }[] = [
  { status: 'Sent', label: 'Sent' },
  { status: 'Logged', label: 'Logged only' },
  { status: 'Failed', label: 'Failed' },
  { status: 'Paused', label: 'Paused' },
  { status: 'Queued', label: 'Waiting' },
];

export function emailStatusLabel(status: EmailStatus): string {
  return EMAIL_STATUSES.find((entry) => entry.status === status)?.label ?? status;
}

/** Every email in the log, whatever its status, for the "All" filter's count. */
export function totalOf(counts: Readonly<Partial<Record<EmailStatus, number>>>): number {
  return Object.values(counts).reduce<number>((sum, count) => sum + (count ?? 0), 0);
}
