import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import type { PayrollDashboard, RunItem } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi } from '@gggs/shared/money';
import { Chart, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { financialYearLabel, financialYearOf, message, recentFinancialYears, thisMonth } from './payroll-shared';

/** The payroll dashboard (spec §103): the latest run, the cost trend, statutory totals and cost by department. */
@Component({
  selector: 'gggs-payroll-dashboard-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, KpiCard, Chart, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payroll" subtitle="This month's run, the cost of pay over time, and what is owed to the authorities.">
      @if (canProcess) {
        <a class="btn btn--primary" routerLink="/payroll/runs">Payroll runs</a>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="pay__kpis">
        <gggs-kpi-card label="On payroll" [value]="d.employeesWithSalary" icon="pi pi-users" [hint]="d.employeesWithoutSalary ? d.employeesWithoutSalary + ' active without a salary' : 'everyone has a salary'" />
        <gggs-kpi-card label="Last net pay" [value]="d.lastFinalized ? (d.lastFinalized.net | gggsMoney: d.lastFinalized.currency) : '—'" [hint]="d.lastFinalized?.label" />
        <gggs-kpi-card label="Last employer cost" [value]="d.lastFinalized ? (d.lastFinalized.employerCost | gggsMoney: d.lastFinalized.currency) : '—'" />
        <gggs-kpi-card label="Declarations to verify" [value]="d.pendingDeclarations" />
        <gggs-kpi-card label="Inputs waiting" [value]="d.pendingInputs" hint="this month onwards" />
      </div>

      @if (d.latestRun; as run) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="pay__heading">Latest run: {{ run.label }}</h2>
            <gggs-status-badge [status]="run.status" />
            <span class="pay__spacer"></span>
            <a class="btn btn--sm" [routerLink]="['/payroll/runs', run.id]">Open</a>
          </div>
          <p class="muted">{{ run.employeeCount }} people · net {{ run.net | gggsMoney: run.currency }} · pay date {{ run.payDate | date: 'd MMM y' }}{{ run.errors ? ' · ' + run.errors + ' with errors' : '' }}</p>
        </section>
      }

      @if (d.trend.length) {
        <section class="panel stack">
          <h2 class="pay__heading">Payroll cost</h2>
          <gggs-chart kind="bar" title="Net pay and employer cost by month" [labels]="labels()" [series]="series()" />
        </section>
      }

      <div class="pay__two">
        @if (d.departments.length) {
          <section class="panel stack">
            <h2 class="pay__heading">Cost by department ({{ d.lastFinalized?.label }})</h2>
            <table class="pay__table">
              <thead><tr><th scope="col">Department</th><th scope="col" class="pay__num">People</th><th scope="col" class="pay__num">Gross</th><th scope="col" class="pay__num">Employer cost</th></tr></thead>
              <tbody>
                @for (row of d.departments; track row.name) {
                  <tr><td>{{ row.name }}</td><td class="pay__num">{{ row.employees }}</td><td class="pay__num">{{ row.gross | gggsMoney }}</td><td class="pay__num">{{ row.employerCost | gggsMoney }}</td></tr>
                }
              </tbody>
            </table>
          </section>
        }
        @if (d.statutory; as s) {
          <section class="panel stack">
            <h2 class="pay__heading">Statutory dues ({{ d.lastFinalized?.label }})</h2>
            <table class="pay__table">
              <tbody>
                <tr><td>Provident fund (employee + employer)</td><td class="pay__num">{{ s.pfEmployee + s.pfEmployer | gggsMoney }}</td></tr>
                <tr><td>ESI (employee + employer)</td><td class="pay__num">{{ s.esiEmployee + s.esiEmployer | gggsMoney }}</td></tr>
                <tr><td>Professional tax</td><td class="pay__num">{{ s.professionalTax | gggsMoney }}</td></tr>
                <tr><td>TDS on salary</td><td class="pay__num">{{ s.tds | gggsMoney }}</td></tr>
                <tr><td>Labour welfare fund</td><td class="pay__num">{{ s.labourWelfare | gggsMoney }}</td></tr>
              </tbody>
            </table>
            <a class="btn btn--link btn--sm" routerLink="/compliance/filings">Filing calendar</a>
          </section>
        }
      </div>
      @if (!d.latestRun) {
        <gggs-empty-state icon="pi pi-play" title="No payroll has been run yet" description="Assign salaries, then open the first month's run." />
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './payroll.css',
})
export class PayrollDashboardPage {
  protected readonly canProcess = inject(PermissionStore).has('payroll.run.view');
  protected readonly dashboard = signal<PayrollDashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly labels = computed(() => this.dashboard()?.trend.map((point) => point.label) ?? []);
  protected readonly series = computed(() => {
    const trend = this.dashboard()?.trend ?? [];
    return [
      { label: 'Net pay', values: trend.map((point) => point.net) },
      { label: 'Employer cost', values: trend.map((point) => point.employerCost) },
    ];
  });

  constructor() {
    inject(PayrollApi)
      .dashboard()
      .then(
        (dashboard) => this.dashboard.set(dashboard),
        (error: unknown) => this.error.set(message(error, 'The payroll dashboard could not be loaded.')),
      );
  }
}

/** Payroll runs (spec §21): one a month, from draft to paid. */
@Component({
  selector: 'gggs-payroll-runs-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payroll runs" subtitle="Each month's payroll: calculate, review, approve, finalize, pay.">
      @if (canProcess) {
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> New run</button>
      }
    </gggs-page-header>
    <section class="panel toolbar pay__filters">
      <label class="field">
        <span class="field__label">Financial year</span>
        <select [value]="year()" (change)="year.set(+$any($event.target).value); load()">
          @for (option of years; track option) {
            <option [value]="option">{{ yearLabel(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (run of runs(); track run.id) {
        <a class="pay__row" [routerLink]="['/payroll/runs', run.id]">
          <strong>{{ run.label }}</strong>
          <span>{{ run.employeeCount }} people</span>
          <span>Net {{ run.net | gggsMoney: run.currency }}</span>
          <span class="muted">Pay date {{ run.payDate | date: 'd MMM y' }}{{ run.errors ? ' · ' + run.errors + ' errors' : '' }}</span>
          <gggs-status-badge [status]="run.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-play" title="No runs in this year" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="New payroll run" saveLabel="Create" [busy]="busy()" [error]="formError()" [saveDisabled]="!period()" (save)="create()">
      <label class="field">
        <span class="field__label">Month</span>
        <input type="month" [value]="period().slice(0, 7)" (change)="period.set($any($event.target).value ? $any($event.target).value + '-01' : '')" />
      </label>
      <label class="field">
        <span class="field__label">Pay date (optional)</span>
        <input type="date" [value]="payDate()" (change)="payDate.set($any($event.target).value)" />
      </label>
      <p class="muted">Leave the pay date empty to use the pay day in payroll settings.</p>
    </gggs-form-dialog>
  `,
  styleUrl: './payroll.css',
})
export class PayrollRunsPage {
  private readonly api = inject(PayrollApi);
  private readonly router = inject(Router);

  protected readonly canProcess = inject(PermissionStore).has('payroll.run.process');
  protected readonly years = recentFinancialYears();
  protected readonly yearLabel = financialYearLabel;
  protected readonly year = signal(financialYearOf(thisMonth()));
  protected readonly runs = signal<readonly RunItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly period = signal(thisMonth());
  protected readonly payDate = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected openNew(): void {
    this.period.set(thisMonth());
    this.payDate.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const run = await this.api.createRun({ period: this.period(), payDate: this.payDate() || null });
      this.dialogOpen.set(false);
      void this.router.navigate(['/payroll/runs', run.id]);
    } catch (error) {
      this.formError.set(message(error, 'The run could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.runs.set(await this.api.runs(this.year()));
    } catch (error) {
      this.error.set(message(error, 'Runs could not be loaded.'));
    }
  }
}
