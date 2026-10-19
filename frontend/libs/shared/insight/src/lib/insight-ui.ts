import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { Metric } from '@gggs/shared/models';
import { Chart } from '@gggs/shared/ui';

/** A metric's value as people read it: 12.3%, 1,23,456, 7.5 h, 21 days. */
export function formatMetric(metric: Pick<Metric, 'unit' | 'value'>): string {
  const value = metric.value;
  switch (metric.unit) {
    case 'Percent':
      return `${round(value, 1)}%`;
    case 'Currency':
      return Math.round(value).toLocaleString('en-IN');
    case 'Hours':
      return `${round(value, 1)} h`;
    case 'Days':
      return `${round(value, 1)} days`;
    case 'Score':
      return `${round(value, 2)}`;
    default:
      return Math.round(value).toLocaleString('en-IN');
  }
}

function round(value: number, places: number): string {
  const factor = 10 ** places;
  return `${Math.round(value * factor) / factor}`;
}

/** Turns rows into a CSV file and hands it to the browser. */
export function downloadCsv(fileName: string, rows: readonly (readonly (string | number)[])[]): void {
  const text = rows.map((row) => row.map((cell) => (/[",\n]/.test(`${cell}`) ? `"${`${cell}`.replace(/"/g, '""')}"` : `${cell}`)).join(',')).join('\n');
  downloadFile(fileName, 'text/csv', new Blob([text], { type: 'text/csv' }));
}

/** Saves a blob (or base64 content) under a file name. */
export function downloadFile(fileName: string, contentType: string, content: Blob | string): void {
  const blob = typeof content === 'string' ? new Blob([Uint8Array.from(atob(content), (c) => c.charCodeAt(0))], { type: contentType }) : content;
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

/**
 * One metric: the headline value, its hint, and - when it has them - a breakdown as bars and a trend
 * as a line, so a dashboard is just a grid of these.
 */
@Component({
  selector: 'gggs-metric-panel',
  imports: [Chart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="metric" [class.metric--wide]="wide()">
      <header class="metric__head">
        <span class="metric__label">{{ metric().label }}</span>
        <strong class="metric__value">{{ value() }}</strong>
        @if (metric().hint) {
          <span class="metric__hint">{{ metric().hint }}</span>
        }
      </header>
      @if (trend().length > 1) {
        <gggs-chart kind="line" [labels]="trendLabels()" [series]="[{ label: metric().label, values: trendValues() }]" height="9rem" />
      }
      @if (breakdown().length) {
        <ul class="metric__bars">
          @for (point of breakdown(); track point.label) {
            <li>
              <span class="metric__bar-label" [title]="point.label">{{ point.label }}</span>
              <span class="metric__bar"><span [style.width.%]="share(point.value)"></span></span>
              <span class="metric__bar-value">{{ format(point.value) }}</span>
            </li>
          }
        </ul>
      }
    </article>
  `,
  styles: `
    .metric {
      display: grid;
      gap: var(--gggs-space-2);
      align-content: start;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md, 0.5rem);
      background: var(--gggs-color-surface, transparent);
    }

    .metric__head {
      display: grid;
      gap: var(--gggs-space-1);
    }

    .metric__label {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .metric__value {
      font-size: 1.75rem;
      line-height: 1.1;
    }

    .metric__hint {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .metric__bars {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--gggs-space-1);
    }

    .metric__bars li {
      display: grid;
      grid-template-columns: minmax(6rem, 1fr) 2fr auto;
      gap: var(--gggs-space-2);
      align-items: center;
      font-size: var(--gggs-font-size-sm);
    }

    .metric__bar-label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .metric__bar {
      height: 0.5rem;
      border-radius: 999px;
      background: var(--gggs-color-border);
      overflow: hidden;
    }

    .metric__bar span {
      display: block;
      height: 100%;
      background: var(--gggs-color-primary);
    }
  `,
})
export class MetricPanel {
  readonly metric = input.required<Metric>();
  readonly wide = input(false);

  protected readonly value = computed(() => formatMetric(this.metric()));
  protected readonly trend = computed(() => this.metric().trend ?? []);
  protected readonly trendLabels = computed(() => this.trend().map((point) => point.label));
  protected readonly trendValues = computed(() => this.trend().map((point) => point.value));
  protected readonly breakdown = computed(() => (this.metric().breakdown ?? []).slice(0, 10));
  private readonly max = computed(() => Math.max(1, ...this.breakdown().map((point) => Math.abs(point.value))));

  protected share(value: number): number {
    return Math.round((Math.abs(value) / this.max()) * 100);
  }

  protected format(value: number): string {
    return formatMetric({ unit: this.metric().unit === 'Percent' ? 'Percent' : this.metric().unit === 'Currency' ? 'Currency' : 'Count', value });
  }
}
