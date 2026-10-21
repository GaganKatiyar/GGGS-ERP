import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, model, signal } from '@angular/core';
import { ChartModule } from 'primeng/chart';
import { SkeletonModule } from 'primeng/skeleton';

export interface TimelineEntry {
  readonly date: string;
  readonly title: string;
  readonly description?: string;
  readonly tone?: 'neutral' | 'primary' | 'success' | 'warning' | 'danger';
}

/** A vertical list of dated events - employment history, approvals, audit trails. Newest first is the caller's choice. */
@Component({
  selector: 'gggs-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="timeline">
      @for (entry of entries(); track $index) {
        <li class="timeline__item" [attr.data-tone]="entry.tone ?? 'neutral'">
          <span class="timeline__dot" aria-hidden="true"></span>
          <div>
            <p class="timeline__title">{{ entry.title }}</p>
            <p class="timeline__date">{{ when(entry.date) }}</p>
            @if (entry.description) {
              <p class="timeline__description">{{ entry.description }}</p>
            }
          </div>
        </li>
      }
    </ol>
  `,
  styles: `
    .timeline {
      position: relative;
      margin: 0;
      padding: 0 0 0 var(--gggs-space-6);
      list-style: none;
      border-inline-start: 2px solid var(--gggs-color-border);
    }

    .timeline__item {
      position: relative;
      padding-block-end: var(--gggs-space-5);
    }

    .timeline__dot {
      position: absolute;
      inset-inline-start: calc(-1 * var(--gggs-space-6) - 7px);
      top: 0.25rem;
      width: 12px;
      height: 12px;
      border: 2px solid var(--gggs-color-surface-raised);
      border-radius: 50%;
      background: var(--gggs-color-text-muted);
    }

    [data-tone='primary'] .timeline__dot {
      background: var(--gggs-color-primary);
    }

    [data-tone='success'] .timeline__dot {
      background: var(--gggs-color-success);
    }

    [data-tone='warning'] .timeline__dot {
      background: var(--gggs-color-warning-strong);
    }

    [data-tone='danger'] .timeline__dot {
      background: var(--gggs-color-danger);
    }

    .timeline__title {
      margin: 0;
      font-weight: var(--gggs-font-weight-semibold);
    }

    .timeline__date,
    .timeline__description {
      margin: var(--gggs-space-1) 0 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }
  `,
})
export class Timeline {
  /** Timestamps read as a local date and time, plain dates as a date; anything else as given. */
  protected when(value: string): string {
    if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) {
      const utc = /[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`;
      return new Date(utc).toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    }

    if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
    }

    return value;
  }

  readonly entries = input.required<readonly TimelineEntry[]>();
}

/** Placeholder lines while something loads, so the page keeps its shape instead of jumping. */
@Component({
  selector: 'gggs-skeleton',
  imports: [SkeletonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="skeleton" role="status" aria-label="Loading">
      @for (line of lineArray(); track $index) {
        <p-skeleton [width]="$last ? '60%' : '100%'" [height]="height()" />
      }
    </div>
  `,
  styles: `
    .skeleton {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
    }
  `,
})
export class Skeleton {
  readonly lines = input(3);
  readonly height = input('1rem');
  protected readonly lineArray = computed(() => Array.from({ length: this.lines() }));
}

export type ChartKind = 'bar' | 'line' | 'doughnut' | 'pie';

export interface ChartSeries {
  readonly label: string;
  readonly values: readonly number[];
}

/**
 * A chart from labels and series - the caller never builds Chart.js configuration. Colours come
 * from the design tokens so charts follow the theme, including dark mode.
 */
@Component({
  selector: 'gggs-chart',
  imports: [ChartModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="chart" [attr.aria-label]="title()">
      @if (title()) {
        <figcaption class="chart__title">{{ title() }}</figcaption>
      }
      <p-chart [type]="kind()" [data]="data()" [options]="options()" [height]="height()" [ariaLabel]="title() || 'Chart'" />
    </figure>
  `,
  styles: `
    .chart {
      margin: 0;
    }

    .chart__title {
      margin-block-end: var(--gggs-space-2);
      font-weight: var(--gggs-font-weight-semibold);
    }
  `,
})
export class Chart {
  readonly kind = input<ChartKind>('bar');
  readonly title = input('');
  readonly labels = input.required<readonly string[]>();
  readonly series = input.required<readonly ChartSeries[]>();
  readonly height = input('16rem');

  /** Used when the tokens cannot be read (tests, server rendering). */
  private static readonly fallback = ['#1d4ed8', '#0e9f6e', '#d97706', '#7c3aed', '#db2777', '#0891b2', '#65a30d', '#475569'];

  /** Bumped when the theme class on <html> changes, so colours are read again. */
  private readonly theme = signal(0);

  constructor() {
    if (typeof MutationObserver !== 'undefined' && typeof document !== 'undefined') {
      const observer = new MutationObserver(() => this.theme.update((tick) => tick + 1));
      observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
      inject(DestroyRef).onDestroy(() => observer.disconnect());
    }
  }

  /** The categorical palette and axis colours, from the design tokens (a canvas cannot read CSS variables). */
  private readonly colours = computed(() => {
    this.theme();
    const style = typeof document === 'undefined' ? null : getComputedStyle(document.documentElement);
    const token = (name: string, fallback: string) => style?.getPropertyValue(name).trim() || fallback;
    return {
      palette: Chart.fallback.map((colour, index) => token(`--gggs-cat-${index + 1}`, colour)),
      text: token('--gggs-color-text-muted', '#5b6577'),
      grid: token('--gggs-color-border', '#e3e7ee'),
      font: token('--gggs-font-family', 'system-ui, sans-serif'),
    };
  });

  protected readonly data = computed(() => {
    const circular = this.kind() === 'doughnut' || this.kind() === 'pie';
    const palette = this.colours().palette;
    return {
      labels: [...this.labels()],
      datasets: this.series().map((series, index) => ({
        label: series.label,
        data: [...series.values],
        backgroundColor: circular ? palette : palette[index % palette.length],
        borderColor: circular ? undefined : palette[index % palette.length],
        borderRadius: this.kind() === 'bar' ? 4 : undefined,
        maxBarThickness: 56,
        tension: 0.3,
      })),
    };
  });

  protected readonly options = computed(() => {
    const { text, grid, font } = this.colours();
    const circular = this.kind() === 'doughnut' || this.kind() === 'pie';
    const nonNegative = this.series().every((series) => series.values.every((value) => value >= 0));
    const axis = { ticks: { color: text, font: { family: font, size: 11 } }, grid: { color: grid }, border: { display: false } };
    return {
      maintainAspectRatio: false,
      font: { family: font },
      plugins: {
        legend: { display: this.series().length > 1 || circular, labels: { color: text, font: { family: font }, usePointStyle: true, boxWidth: 8 } },
        tooltip: { padding: 10, cornerRadius: 6, titleFont: { family: font }, bodyFont: { family: font } },
      },
      // Counts and money never go negative: start the axis at zero so an all-zero series is a flat
      // line on the floor rather than floating in the middle of a -1..1 scale.
      scales: circular ? undefined : { x: { ...axis, grid: { display: false } }, y: nonNegative ? { ...axis, min: 0, suggestedMax: 1 } : axis },
    };
  });
}

export interface TabItem {
  readonly key: string;
  readonly label: string;
  /** Shown but not selectable - e.g. a module that has not shipped yet. */
  readonly disabled?: boolean;
  /** Short note next to the label, such as a milestone or a count. */
  readonly note?: string;
}

/**
 * A row of tabs. The content is the page's: it switches on the selected key. Keyboard support
 * follows the ARIA tabs pattern (arrow keys move between enabled tabs).
 */
@Component({
  selector: 'gggs-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tabs" role="tablist">
      @for (tab of tabs(); track tab.key) {
        <button
          type="button"
          role="tab"
          class="tabs__tab"
          [class.tabs__tab--active]="tab.key === selected()"
          [attr.aria-selected]="tab.key === selected()"
          [attr.tabindex]="tab.key === selected() ? 0 : -1"
          [disabled]="tab.disabled"
          (click)="selected.set(tab.key)"
          (keydown)="onKey($event)"
        >
          {{ tab.label }}
          @if (tab.note) {
            <span class="tabs__note">{{ tab.note }}</span>
          }
        </button>
      }
    </div>
  `,
  styles: `
    .tabs {
      display: flex;
      gap: var(--gggs-space-1);
      overflow-x: auto;
      scrollbar-width: none;
      border-block-end: 1px solid var(--gggs-color-border);
      margin-block-end: var(--gggs-space-4);
    }

    .tabs__tab {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-1);
      margin-block-end: -1px;
      padding: var(--gggs-space-3) var(--gggs-space-3) calc(var(--gggs-space-3) - 1px);
      border: 0;
      border-block-end: 2px solid transparent;
      background: transparent;
      color: var(--gggs-color-text-muted);
      font: inherit;
      font-weight: var(--gggs-font-weight-medium);
      white-space: nowrap;
      cursor: pointer;
      transition: color var(--gggs-transition-fast), border-color var(--gggs-transition-fast);
    }

    .tabs__tab:hover:not(:disabled) {
      color: var(--gggs-color-text);
      border-block-end-color: var(--gggs-color-border-strong);
    }

    .tabs__tab:focus-visible {
      outline: none;
      box-shadow: inset var(--gggs-focus-ring);
      border-radius: var(--gggs-radius-sm) var(--gggs-radius-sm) 0 0;
    }

    .tabs__tab--active,
    .tabs__tab--active:hover:not(:disabled) {
      border-block-end-color: var(--gggs-color-primary);
      color: var(--gggs-color-primary);
    }

    .tabs__tab:disabled {
      cursor: not-allowed;
      opacity: 0.55;
    }

    .tabs__note {
      padding: 0 0.4rem;
      border-radius: var(--gggs-radius-pill);
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class Tabs {
  readonly tabs = input.required<readonly TabItem[]>();
  readonly selected = model<string>('');

  protected onKey(event: KeyboardEvent): void {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') {
      return;
    }

    const enabled = this.tabs().filter((tab) => !tab.disabled);
    const index = enabled.findIndex((tab) => tab.key === this.selected());
    const step = event.key === 'ArrowRight' ? 1 : -1;
    const next = enabled[(index + step + enabled.length) % enabled.length];

    if (next) {
      this.selected.set(next.key);
      event.preventDefault();
    }
  }
}
