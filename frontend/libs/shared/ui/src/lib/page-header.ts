import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Standard page heading: title, optional subtitle and a slot for page actions.
 *
 * Every list and detail screen uses this, so the primary action always sits in the same place and
 * the heading level stays consistent for screen readers.
 */
@Component({
  selector: 'gggs-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="page-header">
      <div class="page-header__text">
        <h1 class="page-header__title">{{ title() }}</h1>
        @if (subtitle()) {
          <p class="page-header__subtitle">{{ subtitle() }}</p>
        }
      </div>

      <div class="page-header__actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: `
    .page-header {
      display: flex;
      align-items: flex-start;
      gap: 1rem;
      margin-block-end: 1rem;
    }

    .page-header__text {
      flex: 1;
      min-width: 0;
    }

    .page-header__title {
      margin: 0;
      font-size: 1.375rem;
    }

    .page-header__subtitle {
      margin: 0.25rem 0 0;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .page-header__actions {
      display: flex;
      gap: 0.5rem;
      flex-shrink: 0;
    }
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string | undefined>(undefined);
}
