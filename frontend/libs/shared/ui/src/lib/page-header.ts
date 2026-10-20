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
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--gggs-space-3) var(--gggs-space-4);
      margin-block-end: var(--gggs-space-5);
    }

    .page-header__text {
      flex: 1 1 18rem;
      min-width: 0;
    }

    .page-header__title {
      margin: 0;
      color: var(--gggs-color-text);
      font-size: var(--gggs-font-size-xl);
      font-weight: var(--gggs-font-weight-semibold);
      letter-spacing: -0.015em;
      line-height: var(--gggs-line-height-tight);
    }

    .page-header__subtitle {
      margin: var(--gggs-space-1) 0 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-base);
    }

    .page-header__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .page-header__actions:empty {
      display: none;
    }
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string | undefined>(undefined);
}
