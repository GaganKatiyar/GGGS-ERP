import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type KpiTrend = 'up' | 'down' | 'flat';

/**
 * One headline number on a dashboard: a label, the value, and optionally what it means.
 *
 * `trendIsGood` decides the colour of the trend arrow, because "up" is good for headcount and bad
 * for attrition - the card cannot know which, the page does. The arrow's direction carries the
 * meaning too, so it never rests on colour alone.
 *
 * `series` (a few recent values, oldest first) draws a sparkline and `progress` (0-100) a meter;
 * pass them only when the page already has those figures.
 */
@Component({
  selector: 'gggs-kpi-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="kpi" [attr.aria-label]="label()">
      <div class="kpi__head">
        <span class="kpi__label">{{ label() }}</span>
        @if (icon()) {
          <span class="kpi__icon" aria-hidden="true"><i [class]="icon()"></i></span>
        }
      </div>
      <p class="kpi__value">{{ value() ?? '—' }}</p>
      @if (spark(); as spark) {
        <svg class="kpi__spark" viewBox="0 0 100 24" preserveAspectRatio="none" aria-hidden="true">
          <path class="kpi__area" [attr.d]="spark.area" />
          <path class="kpi__line" [attr.d]="spark.line" vector-effect="non-scaling-stroke" />
        </svg>
      }
      @if (meter() !== null) {
        <span class="kpi__meter" role="progressbar" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="meter()" [attr.aria-label]="label()">
          <span [style.width.%]="meter()"></span>
        </span>
      }
      @if (hint() || trend()) {
        <p class="kpi__hint">
          @if (trend()) {
            <span class="kpi__trend" [class.kpi__trend--good]="tone() === 'good'" [class.kpi__trend--bad]="tone() === 'bad'">
              <i [class]="trendIcon()" aria-hidden="true"></i>
            </span>
            <span class="sr-only">{{ trendText() }}</span>
          }
          {{ hint() }}
        </p>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
    }

    .kpi {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      height: 100%;
      min-height: 6.5rem;
      overflow: hidden;
      padding: var(--gggs-space-4) var(--gggs-space-5);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-1), var(--gggs-shadow-highlight);
      transition:
        transform var(--gggs-transition),
        box-shadow var(--gggs-transition),
        border-color var(--gggs-transition);
    }

    /* A lit accent line along the top edge, drawn in when the card rises. */
    .kpi::before {
      content: '';
      position: absolute;
      inset: 0 0 auto;
      height: 2px;
      background: var(--gggs-gradient-accent);
      opacity: 0;
      transition: opacity var(--gggs-transition);
    }

    .kpi:hover {
      transform: translateY(var(--gggs-lift));
      border-color: color-mix(in srgb, var(--gggs-color-primary) 24%, var(--gggs-color-border));
      box-shadow: var(--gggs-shadow-3), var(--gggs-shadow-highlight);
    }

    .kpi:hover::before {
      opacity: 1;
    }

    .kpi__head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--gggs-space-2);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-medium);
    }

    .kpi__icon {
      display: inline-grid;
      flex: 0 0 auto;
      place-items: center;
      width: 2rem;
      height: 2rem;
      margin: calc(-1 * var(--gggs-space-1)) calc(-1 * var(--gggs-space-1)) 0 0;
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-gradient-tile);
      box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--gggs-color-primary) 14%, transparent);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-base);
    }

    .kpi__value {
      margin: 0;
      color: var(--gggs-color-text);
      font-size: var(--gggs-font-size-2xl);
      font-weight: var(--gggs-font-weight-semibold);
      font-variant-numeric: tabular-nums;
      letter-spacing: -0.02em;
      line-height: var(--gggs-line-height-tight);
    }

    .kpi__spark {
      display: block;
      width: 100%;
      height: 1.75rem;
      overflow: visible;
    }

    .kpi__line {
      fill: none;
      stroke: var(--gggs-color-primary);
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }

    .kpi__area {
      fill: color-mix(in srgb, var(--gggs-color-accent-bright) 14%, transparent);
    }

    .kpi__meter {
      display: block;
      height: 0.375rem;
      overflow: hidden;
      border-radius: var(--gggs-radius-pill);
      background: var(--gggs-color-surface-sunken);
    }

    .kpi__meter > span {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: var(--gggs-gradient-accent);
    }

    .kpi__hint {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
      margin: auto 0 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
    }

    .kpi__trend {
      display: inline-grid;
      flex: 0 0 auto;
      place-items: center;
      width: 1.25rem;
      height: 1.25rem;
      border-radius: var(--gggs-radius-pill);
      background: var(--gggs-color-surface-sunken);
      font-size: var(--gggs-font-size-xs);
    }

    .kpi__trend--good {
      background: var(--gggs-color-success-subtle);
      color: var(--gggs-color-success);
    }

    .kpi__trend--bad {
      background: var(--gggs-color-danger-subtle);
      color: var(--gggs-color-danger);
    }
  `,
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input<string | number | null>(null);
  readonly hint = input<string | undefined>(undefined);
  readonly icon = input<string | undefined>(undefined);
  readonly trend = input<KpiTrend | undefined>(undefined);
  readonly trendIsGood = input(true);
  /** Recent values, oldest first, drawn as a sparkline when there are at least two. */
  readonly series = input<readonly number[]>([]);
  /** A share of a whole, 0 to 100, drawn as a meter under the value. */
  readonly progress = input<number | null | undefined>(undefined);

  protected readonly trendIcon = computed(() => {
    switch (this.trend()) {
      case 'up':
        return 'pi pi-arrow-up-right';
      case 'down':
        return 'pi pi-arrow-down-right';
      default:
        return 'pi pi-minus';
    }
  });

  /** The arrow in words, for screen readers. */
  protected readonly trendText = computed(() => {
    const direction = this.trend() === 'up' ? 'Up' : this.trend() === 'down' ? 'Down' : 'No change';
    const tone = this.tone();
    return tone === 'neutral' ? `${direction}.` : `${direction}, ${tone === 'good' ? 'an improvement' : 'a decline'}.`;
  });

  protected readonly tone = computed(() => {
    const trend = this.trend();
    if (!trend || trend === 'flat') {
      return 'neutral';
    }
    return (trend === 'up') === this.trendIsGood() ? 'good' : 'bad';
  });

  protected readonly meter = computed(() => {
    const progress = this.progress();
    return progress === null || progress === undefined || Number.isNaN(progress) ? null : Math.round(Math.min(100, Math.max(0, progress)));
  });

  /** The sparkline's line and the area under it, in a 100 x 24 box with a little headroom. */
  protected readonly spark = computed(() => sparkPaths(this.series()));
}

export function sparkPaths(values: readonly number[]): { line: string; area: string } | null {
  if (values.length < 2) {
    return null;
  }

  const min = Math.min(...values);
  const range = Math.max(...values) - min || 1;
  const points = values.map((value, index) => {
    const x = (index / (values.length - 1)) * 100;
    const y = 22 - ((value - min) / range) * 20;
    return `${x.toFixed(2)} ${y.toFixed(2)}`;
  });
  const line = `M${points.join(' L')}`;
  return { line, area: `${line} L100 24 L0 24 Z` };
}
