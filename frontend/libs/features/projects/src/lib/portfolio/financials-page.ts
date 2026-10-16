import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { PortfolioFinance } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { EmptyState, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** Portfolio financials (spec §76-77): revenue, cost and margin across projects, worst margins first. */
@Component({
  selector: 'gggs-portfolio-financials-page',
  imports: [DecimalPipe, RouterLink, MoneyPipe, PageHeader, KpiCard, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Project financials" subtitle="Revenue, cost and margin across the projects you can see." />
    @if (portfolio(); as p) {
      <div class="fin__kpis">
        <gggs-kpi-card label="Contract value" [value]="p.contractValue | gggsMoney" icon="pi pi-briefcase" />
        <gggs-kpi-card label="Revenue to date" [value]="p.revenue | gggsMoney" />
        <gggs-kpi-card label="Cost to date" [value]="p.actualCost | gggsMoney" />
        <gggs-kpi-card label="Gross margin" [value]="p.marginPercent !== undefined ? (p.marginPercent | number: '1.0-1') + '%' : '—'" [hint]="p.grossMargin | gggsMoney" />
        <gggs-kpi-card label="Unbilled" [value]="p.unbilled | gggsMoney" [hint]="(p.billed | gggsMoney) + ' billed'" />
        <gggs-kpi-card label="Watch" [value]="p.overBudget + p.lossMaking" [hint]="p.overBudget + ' over budget · ' + p.lossMaking + ' losing money'" />
      </div>
      <section class="panel fin__scroll">
        <table class="fin__table">
          <thead>
            <tr>
              <th scope="col">Project</th><th scope="col">Billing</th><th scope="col" class="fin__num">Contract</th><th scope="col" class="fin__num">Revenue</th>
              <th scope="col" class="fin__num">Cost</th><th scope="col" class="fin__num">Margin</th><th scope="col" class="fin__num">Budget used</th><th scope="col" class="fin__num">Unbilled</th><th scope="col" class="fin__num">Forecast margin</th>
            </tr>
          </thead>
          <tbody>
            @for (row of p.projects; track row.projectId) {
              <tr>
                <th scope="row"><a [routerLink]="['/projects', row.projectId]" [queryParams]="{ tab: 'financials' }"><strong>{{ row.code }}</strong> {{ row.name }}</a> <gggs-status-badge [status]="row.status" /></th>
                <td>{{ humanize(row.billingType) }}</td>
                <td class="fin__num">{{ row.contractValue | gggsMoney: row.currency }}</td>
                <td class="fin__num">{{ row.revenue | gggsMoney: row.currency }}</td>
                <td class="fin__num">{{ row.actualCost | gggsMoney: row.currency }}</td>
                <td class="fin__num" [class.fin__danger]="row.grossMargin < 0">{{ row.marginPercent !== undefined ? (row.marginPercent | number: '1.0-1') + '%' : '—' }}</td>
                <td class="fin__num" [class.fin__danger]="(row.budgetUsedPercent ?? 0) > 100">{{ row.budgetUsedPercent !== undefined ? (row.budgetUsedPercent | number: '1.0-0') + '%' : '—' }}</td>
                <td class="fin__num">{{ row.unbilled | gggsMoney: row.currency }}</td>
                <td class="fin__num" [class.fin__danger]="row.forecastMargin < 0">{{ row.forecastMarginPercent !== undefined ? (row.forecastMarginPercent | number: '1.0-1') + '%' : '—' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="9"><gggs-empty-state icon="pi pi-money-bill" title="No projects to show" /></td></tr>
            }
          </tbody>
        </table>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    .fin__kpis {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      gap: var(--gggs-space-3);
      margin-block-end: var(--gggs-space-3);
    }

    .fin__scroll {
      overflow-x: auto;
    }

    .fin__table {
      width: 100%;
      border-collapse: collapse;
    }

    .fin__table th,
    .fin__table td {
      padding: var(--gggs-space-1) var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .fin__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .fin__danger {
      color: var(--gggs-color-danger, #dc2626);
    }
  `,
})
export class PortfolioFinancialsPage {
  protected readonly humanize = humanize;
  protected readonly portfolio = signal<PortfolioFinance | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(ProjectFinancialsApi)
      .portfolio()
      .then(
        (portfolio) => this.portfolio.set(portfolio),
        (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'Financials could not be loaded.'),
      );
  }
}
