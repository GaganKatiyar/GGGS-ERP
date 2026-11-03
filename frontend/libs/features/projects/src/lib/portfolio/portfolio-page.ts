import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { CurrencyPortfolio, PortfolioDashboard, RollupRow } from '@gggs/shared/models';
import { BILLING_MODEL_LABELS, type BillingModel, humanize } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { Chart, EmptyState, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

interface RollupTable {
  readonly title: string;
  readonly heading: string;
  readonly rows: readonly RollupRow[];
}

/** The portfolio for leadership: delivery health, margin by engagement model, client and department, and revenue still to come. */
@Component({
  selector: 'gggs-portfolio-page',
  imports: [DatePipe, DecimalPipe, RouterLink, MoneyPipe, PageHeader, KpiCard, Chart, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Portfolio" subtitle="Delivery health and margin across the projects you can see, and the revenue still to come." />
    @if (dashboard(); as d) {
      <div class="stack">
        <div class="xp__kpis">
          <gggs-kpi-card label="On track" [value]="d.health.green" hint="Green" icon="pi pi-check-circle" />
          <gggs-kpi-card label="At risk" [value]="d.health.amber" hint="Amber" icon="pi pi-exclamation-circle" />
          <gggs-kpi-card label="In trouble" [value]="d.health.red" hint="Red" icon="pi pi-times-circle" />
          <gggs-kpi-card label="Not yet assessed" [value]="d.health.notAssessed" icon="pi pi-question-circle" />
        </div>

        @if (d.currencies.length > 1) {
          <div class="pf__currencies">
            <label class="field">
              <span class="field__label">Currency</span>
              <select [value]="currency()?.currency" (change)="chosen.set($any($event.target).value)">
                @for (row of d.currencies; track row.currency) {
                  <option [value]="row.currency" [selected]="row.currency === currency()?.currency">{{ row.currency }}</option>
                }
              </select>
            </label>
            <p class="muted pf__note">Each currency is shown on its own: the app holds no exchange rates, so these are never added together.</p>
          </div>
        }

        @if (currency(); as c) {
          <div class="xp__kpis fin__kpis">
            <gggs-kpi-card label="Revenue to date" [value]="c.totals.revenue | gggsMoney: c.currency" [hint]="c.totals.projects + (c.totals.projects === 1 ? ' project' : ' projects')" />
            <gggs-kpi-card label="Cost to date" [value]="c.totals.cost | gggsMoney: c.currency" />
            <gggs-kpi-card label="Margin" [value]="c.totals.marginPercent !== undefined ? (c.totals.marginPercent | number: '1.0-1') + '%' : 'None yet'" [hint]="c.totals.margin | gggsMoney: c.currency" />
            <gggs-kpi-card label="Unbilled" [value]="c.totals.unbilled | gggsMoney: c.currency" />
          </div>

          <section class="panel stack">
            <h3 class="pf__heading">Revenue still to come</h3>
            <gggs-chart kind="bar" [title]="'Forecast revenue by month, ' + c.currency" [labels]="forecastLabels()" [series]="forecastSeries()" height="14rem" />
            <p class="muted pf__note">
              What each project has left to earn, spread evenly over the months it has left. It shows the shape of what is coming and is not a commitment.
              @if (c.forecastLater > 0) {
                A further {{ c.forecastLater | gggsMoney: c.currency }} falls after these six months.
              }
            </p>
            <div class="pf__scroll" tabindex="0" role="region" aria-label="Forecast revenue by month">
              <table class="table">
                <thead>
                  <tr>
                    @for (month of c.forecast; track month.month) {
                      <th scope="col" class="pf__num">{{ month.month | date: 'MMM y' }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    @for (month of c.forecast; track month.month) {
                      <td class="pf__num">{{ month.revenue | gggsMoney: c.currency }}</td>
                    }
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          @if (c.watch.length) {
            <section class="panel pf__scroll">
              <h3 class="pf__heading">Worth a look</h3>
              <table class="table">
                <thead>
                  <tr><th scope="col">Project</th><th scope="col">Health</th><th scope="col" class="pf__num">Margin</th><th scope="col" class="pf__num">Margin %</th><th scope="col" class="pf__num">Unbilled</th></tr>
                </thead>
                <tbody>
                  @for (row of c.watch; track row.projectId) {
                    <tr>
                      <th scope="row"><a [routerLink]="['/projects', row.projectId]" [queryParams]="{ tab: 'financials' }"><strong>{{ row.code }}</strong> {{ row.name }}</a></th>
                      <td><gggs-status-badge [status]="row.health" /></td>
                      <td class="pf__num" [class.pf__bad]="row.margin < 0">{{ row.margin | gggsMoney: c.currency }}</td>
                      <td class="pf__num" [class.pf__bad]="row.margin < 0">{{ row.marginPercent !== undefined ? (row.marginPercent | number: '1.0-1') + '%' : '' }}</td>
                      <td class="pf__num">{{ row.unbilled | gggsMoney: c.currency }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }

          @for (table of tables(); track table.title) {
            <section class="panel pf__scroll">
              <h3 class="pf__heading">{{ table.title }}</h3>
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col">{{ table.heading }}</th><th scope="col" class="pf__num">Projects</th><th scope="col" class="pf__num">Revenue</th><th scope="col" class="pf__num">Cost</th>
                    <th scope="col" class="pf__num">Margin</th><th scope="col" class="pf__num">Margin %</th><th scope="col" class="pf__num">Forecast revenue</th><th scope="col" class="pf__num">Unbilled</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of table.rows; track row.label) {
                    <tr>
                      <th scope="row">{{ row.label }}</th>
                      <td class="pf__num">{{ row.projects }}</td>
                      <td class="pf__num">{{ row.revenue | gggsMoney: c.currency }}</td>
                      <td class="pf__num">{{ row.cost | gggsMoney: c.currency }}</td>
                      <td class="pf__num" [class.pf__bad]="row.margin < 0">{{ row.margin | gggsMoney: c.currency }}</td>
                      <td class="pf__num" [class.pf__bad]="row.margin < 0">{{ row.marginPercent !== undefined ? (row.marginPercent | number: '1.0-1') + '%' : '' }}</td>
                      <td class="pf__num">{{ row.forecastRevenue | gggsMoney: c.currency }}</td>
                      <td class="pf__num">{{ row.unbilled | gggsMoney: c.currency }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }
        } @else {
          <section class="panel"><gggs-empty-state icon="pi pi-chart-bar" title="No projects to show" /></section>
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .fin__kpis {
      grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr));
    }

    .pf__currencies {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      align-items: end;
    }

    .pf__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .pf__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .pf__scroll {
      overflow-x: auto;
    }

    .pf__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .pf__bad {
      color: var(--gggs-color-danger);
    }
  `,
})
export class PortfolioPage {
  protected readonly dashboard = signal<PortfolioDashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly chosen = signal<string | null>(null);

  protected readonly currency = computed<CurrencyPortfolio | null>(() => {
    const currencies = this.dashboard()?.currencies ?? [];
    return currencies.find((row) => row.currency === this.chosen()) ?? currencies[0] ?? null;
  });

  protected readonly forecastLabels = computed(() => (this.currency()?.forecast ?? []).map((month) => month.month.slice(0, 7)));
  protected readonly forecastSeries = computed(() => [{ label: 'Forecast revenue', values: (this.currency()?.forecast ?? []).map((month) => month.revenue) }]);

  protected readonly tables = computed<readonly RollupTable[]>(() => {
    const current = this.currency();
    if (!current) {
      return [];
    }

    return [
      { title: 'By engagement model', heading: 'Model', rows: current.byModel.map((row) => ({ ...row, label: BILLING_MODEL_LABELS[row.label as BillingModel] ?? humanize(row.label) })) },
      { title: 'By client', heading: 'Client', rows: current.byClient },
      { title: 'By department', heading: 'Department', rows: current.byDepartment },
    ];
  });

  constructor() {
    inject(ProjectFinancialsApi)
      .portfolioDashboard()
      .then(
        (dashboard) => this.dashboard.set(dashboard),
        (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'The portfolio could not be loaded.'),
      );
  }
}
