import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type KpiTrend = 'up' | 'down' | 'flat';

/**
 * One headline number on a dashboard: a label, the value, and optionally what it means.
 *
 * `trendIsGood` decides the colour of the trend arrow, because "up" is good for headcount and bad
 * for attrition - the card cannot know which, the page does.
 */
@Component({
  selector: 'gggs-kpi-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="kpi" [attr.aria-label]="label()">
      <div class="kpi__head">
        @if (icon()) {
          <i [class]="icon()" aria-hidden="true"></i>
        }
        <span class="kpi__label">{{ label() }}</span>
      </div>
      <p class="kpi__value">{{ value() ?? '—' }}</p>
      @if (hint() || trend()) {
        <p class="kpi__hint" [class.kpi__hint--good]="tone() === 'good'" [class.kpi__hint--bad]="tone() === 'bad'">
          @if (trend()) {
            <i [class]="trendIcon()" aria-hidden="true"></i>
          }
          {{ hint() }}
        </p>
      }
    </section>
  `,
  styles: `
    .kpi {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-4);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-sm);
    }

    .kpi__head {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .kpi__value {
      margin: 0;
      font-size: var(--gggs-font-size-2xl);
      font-weight: var(--gggs-font-weight-bold);
      line-height: var(--gggs-line-height-tight);
    }

    .kpi__hint {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-1);
      margin: 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
    }

    .kpi__hint--good {
      color: var(--gggs-color-success);
    }

    .kpi__hint--bad {
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

  protected readonly tone = computed(() => {
    const trend = this.trend();
    if (!trend || trend === 'flat') {
      return 'neutral';
    }
    return (trend === 'up') === this.trendIsGood() ? 'good' : 'bad';
  });
}
