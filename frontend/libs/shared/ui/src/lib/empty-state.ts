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
      <i class="empty-state__icon {{ icon() }}" aria-hidden="true"></i>
      <p class="empty-state__title">{{ title() }}</p>

      @if (description()) {
        <p class="empty-state__description">{{ description() }}</p>
      }

      @if (actionLabel()) {
        <button
          type="button"
          class="empty-state__action"
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
      gap: 0.5rem;
      padding: 2.5rem 1rem;
      text-align: center;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .empty-state__icon {
      font-size: 1.75rem;
    }

    .empty-state__title {
      margin: 0;
      font-weight: 600;
      color: var(--gggs-color-text, #1f2430);
    }

    .empty-state__description {
      margin: 0;
      max-width: 32rem;
    }

    .empty-state__action {
      margin-block-start: 0.5rem;
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
