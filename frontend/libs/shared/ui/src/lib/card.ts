import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The bordered/raised panel every list and detail page reinvents as an ad hoc `<div>`.
 *
 * Gives every panel the same radius/shadow/spacing instead of each page picking its own, and is the
 * one place that changes if the app ever needs a second visual treatment (e.g. a flatter density
 * mode - architecture.md §12.1 calls for one).
 */
@Component({
  selector: 'gggs-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (title()) {
      <header class="card__header">
        <h2 class="card__title">{{ title() }}</h2>
      </header>
    }
    <div class="card__body">
      <ng-content />
    </div>
  `,
  host: {
    class: 'card',
  },
  styles: `
    .card {
      display: block;
      background: var(--gggs-color-surface-raised);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-lg, 0.75rem);
      box-shadow: var(--gggs-shadow-sm, none);
    }

    .card__header {
      padding: var(--gggs-space-4, 1rem) var(--gggs-space-5, 1.25rem) 0;
    }

    .card__title {
      margin: 0;
      font-size: var(--gggs-font-size-lg, 1.125rem);
      font-weight: var(--gggs-font-weight-semibold, 600);
    }

    .card__body {
      padding: var(--gggs-space-5, 1.25rem);
    }
  `,
})
export class Card {
  readonly title = input<string | undefined>(undefined);
}
