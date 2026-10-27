import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';

/**
 * What a screen shows when there is nothing to show.
 *
 * Empty is not the same as broken, and an empty table with no explanation reads like a bug. This
 * says what is missing and offers the action that fixes it, which matters most on a fresh tenant
 * where almost every screen starts empty.
 */
@Component({
  selector: 'gggs-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty-state" role="status">
      <span class="empty-state__halo" aria-hidden="true">
        <i class="{{ icon() }}"></i>
      </span>
      <p class="empty-state__title">{{ title() }}</p>

      @if (description()) {
        <p class="empty-state__description">{{ description() }}</p>
      }

      @if (actionLabel()) {
        <button
          type="button"
          class="btn btn--primary btn--sm empty-state__action"
          (click)="action.emit()"
        >
          {{ actionLabel() }}
        </button>
      }
    </div>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-10) var(--gggs-space-4);
      text-align: center;
      color: var(--gggs-color-text-muted);
    }

    /* The icon sits on a raised tile, lit like the KPI cards' icons. */
    .empty-state__halo {
      display: grid;
      place-items: center;
      width: 3.25rem;
      height: 3.25rem;
      margin-block-end: var(--gggs-space-1);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-gradient-tile);
      box-shadow:
        var(--gggs-shadow-2),
        inset 0 0 0 1px color-mix(in srgb, var(--gggs-color-primary) 14%, transparent);
      color: var(--gggs-color-primary);
    }

    /* PrimeIcons sizes .pi itself, so the size goes on the icon, not the tile. */
    .empty-state__halo > i {
      font-size: var(--gggs-font-size-lg);
    }

    .empty-state__title {
      margin: 0;
      font-weight: var(--gggs-font-weight-semibold);
      color: var(--gggs-color-text);
    }

    .empty-state__description {
      margin: 0;
      max-width: 32rem;
    }

    .empty-state__action {
      margin-block-start: var(--gggs-space-2);
    }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string | undefined>(undefined);
  readonly icon = input('pi pi-inbox');
  /** Omit to render an empty state with no call to action. */
  readonly actionLabel = input<string | undefined>(undefined);

  readonly action = output<void>();
}
