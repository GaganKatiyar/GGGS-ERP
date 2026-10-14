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
  open: 'primary',
  inprogress: 'primary',
  submitted: 'primary',
  pending: 'warning',
  onnotice: 'warning',
  probation: 'warning',
  draft: 'neutral',
  inactive: 'neutral',
  closed: 'neutral',
  exited: 'neutral',
  rejected: 'danger',
  cancelled: 'danger',
  overdue: 'danger',
  failed: 'danger',
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
