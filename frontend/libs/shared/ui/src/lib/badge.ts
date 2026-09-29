import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TagModule } from 'primeng/tag';

export type BadgeVariant = 'neutral' | 'primary' | 'success' | 'warning' | 'danger';

/**
 * A small status pill ("system", "Active", "Pending"…) - wraps `p-tag` so severity colors stay
 * consistent instead of each page inventing its own `<span class="badge">` with its own palette.
 */
@Component({
  selector: 'gggs-badge',
  imports: [TagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p-tag [value]="label()" [severity]="severity()" [rounded]="true" />`,
})
export class Badge {
  readonly label = input.required<string>();
  readonly variant = input<BadgeVariant>('neutral');

  protected readonly severity = computed(() => {
    switch (this.variant()) {
      case 'primary':
        return 'info';
      case 'success':
        return 'success';
      case 'warning':
        return 'warn';
      case 'danger':
        return 'danger';
      default:
        return 'secondary';
    }
  });
}
