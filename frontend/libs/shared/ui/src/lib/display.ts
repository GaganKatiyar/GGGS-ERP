import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
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
            <p class="timeline__date">{{ entry.date }}</p>
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
      <p-chart [type]="kind()" [data]="data()" [options]="options()" [height]="height()" />
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

  private static readonly palette = ['#1d4ed8', '#0e9f6e', '#d97706', '#7c3aed', '#db2777', '#0891b2', '#65a30d', '#475569'];

  protected readonly data = computed(() => {
    const circular = this.kind() === 'doughnut' || this.kind() === 'pie';
    return {
      labels: [...this.labels()],
      datasets: this.series().map((series, index) => ({
        label: series.label,
        data: [...series.values],
        backgroundColor: circular ? Chart.palette : Chart.palette[index % Chart.palette.length],
        borderColor: circular ? undefined : Chart.palette[index % Chart.palette.length],
        tension: 0.3,
      })),
    };
  });

  protected readonly options = computed(() => ({
    maintainAspectRatio: false,
    plugins: { legend: { display: this.series().length > 1 || this.kind() === 'doughnut' || this.kind() === 'pie' } },
  }));
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
      border-block-end: 1px solid var(--gggs-color-border);
      margin-block-end: var(--gggs-space-4);
    }

    .tabs__tab {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 0;
      border-block-end: 2px solid transparent;
      background: transparent;
      color: var(--gggs-color-text-muted);
      font: inherit;
      white-space: nowrap;
      cursor: pointer;
    }

    .tabs__tab--active {
      border-block-end-color: var(--gggs-color-primary);
      color: var(--gggs-color-text);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .tabs__tab:disabled {
      cursor: not-allowed;
      opacity: 0.55;
    }

    .tabs__note {
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
