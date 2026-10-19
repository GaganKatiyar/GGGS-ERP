import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Badge, type BadgeVariant } from './badge';

/**
 * The colour each common status gets, so "Active" is the same green on every screen.
 * Keys are compared case-insensitively; a page can pass its own map for statuses not listed here.
 */
export const DEFAULT_STATUS_VARIANTS: Readonly<Record<string, BadgeVariant>> = {
  active: 'success',
  approved: 'success',
  completed: 'success',
  confirmed: 'success',
  published: 'success',
  signed: 'success',
  open: 'primary',
  inprogress: 'primary',
  submitted: 'primary',
  pending: 'warning',
  pendingapproval: 'warning',
  requested: 'warning',
  sentback: 'warning',
  onnotice: 'warning',
  probation: 'warning',
  draft: 'neutral',
  inactive: 'neutral',
  closed: 'neutral',
  superseded: 'neutral',
  withdrawn: 'neutral',
  reassigned: 'neutral',
  skipped: 'neutral',
  waiting: 'neutral',
  exited: 'neutral',
  rejected: 'danger',
  cancelled: 'danger',
  declined: 'danger',
  expired: 'danger',
  overdue: 'danger',
  failed: 'danger',
  // Delivery (M5): project health, milestones, sprints, contracts, change requests and RAID.
  green: 'success',
  amber: 'warning',
  red: 'danger',
  notassessed: 'neutral',
  achieved: 'success',
  done: 'success',
  resolved: 'success',
  held: 'success',
  planned: 'primary',
  scheduled: 'primary',
  prospect: 'primary',
  implementing: 'primary',
  testing: 'primary',
  onhold: 'warning',
  accepted: 'warning',
  notstarted: 'neutral',
  released: 'neutral',
  terminated: 'danger',
  // Resourcing (M6): capacity states and requests.
  bench: 'warning',
  partiallyavailable: 'primary',
  fullyallocated: 'success',
  overallocated: 'danger',
  leave: 'neutral',
  partiallyfulfilled: 'primary',
  fulfilled: 'success',
  proposed: 'primary',
  allocated: 'success',
  // Money (M7): payroll runs and results, filings, registrations, revisions and invoices.
  calculated: 'primary',
  finalized: 'success',
  paid: 'success',
  applied: 'success',
  verified: 'success',
  filed: 'success',
  valid: 'success',
  ok: 'success',
  sent: 'primary',
  planning: 'neutral',
  warning: 'warning',
  expiringsoon: 'warning',
  retired: 'neutral',
  noexpiry: 'neutral',
  waived: 'neutral',
  exercised: 'neutral',
  error: 'danger',
  instock: 'success',
  assigned: 'primary',
  inrepair: 'warning',
  lost: 'danger',
  settled: 'success',
  waitingonrequester: 'warning',
  cleared: 'success',
  breached: 'danger',
};

/** A status as a coloured pill, with a readable label ("OnNotice" shows as "On notice"). */
@Component({
  selector: 'gggs-status-badge',
  imports: [Badge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<gggs-badge [label]="label()" [variant]="variant()" />`,
})
export class StatusBadge {
  readonly status = input.required<string>();
  readonly variants = input<Readonly<Record<string, BadgeVariant>>>(DEFAULT_STATUS_VARIANTS);

  protected readonly label = computed(() => {
    const spaced = this.status().replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').toLowerCase();
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  });

  protected readonly variant = computed<BadgeVariant>(() => {
    const key = this.status().replace(/[\s_-]+/g, '').toLowerCase();
    return this.variants()[key] ?? DEFAULT_STATUS_VARIANTS[key] ?? 'neutral';
  });
}
