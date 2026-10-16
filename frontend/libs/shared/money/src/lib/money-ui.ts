import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Pipe, type PipeTransform, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { EmployeeSalaryView, EnrollmentItem, PayslipDocument, PayslipItem, TotalRewards } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, KpiCard, StatusBadge } from '@gggs/shared/ui';
import { BenefitsApi, CompensationApi, PayrollApi } from './money-apis';

const formatters = new Map<string, Intl.NumberFormat>();

/** Formats money the Indian way (lakh grouping), in the given currency, whole units unless asked otherwise. */
export function formatMoney(value: number | null | undefined, currency = 'INR', digits = 0): string {
  if (value === null || value === undefined) {
    return '—';
  }

  const key = `${currency}:${digits}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = new Intl.NumberFormat('en-IN', { style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits });
    formatters.set(key, formatter);
  }

  return formatter.format(value);
}

@Pipe({ name: 'gggsMoney' })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, currency = 'INR', digits = 0): string {
    return formatMoney(value, currency, digits);
  }
}

const message = (error: unknown, fallback: string) => (error instanceof Error && error.message ? error.message : fallback);

/** A published payslip, laid out to read and to print (spec §21). */
@Component({
  selector: 'gggs-payslip-document',
  imports: [DatePipe, DecimalPipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (document(); as d) {
      <article class="slip">
        <header class="slip__head">
          <div>
            <h2 class="slip__employer">{{ d.employerName }}</h2>
            @if (d.employerAddress) {
              <p class="muted slip__small">{{ d.employerAddress }}</p>
            }
          </div>
          <div class="slip__title">
            <strong>Payslip</strong>
            <span>{{ d.periodLabel }}</span>
            <span class="muted slip__small">Paid {{ d.payDate | date: 'd MMM y' }}</span>
          </div>
        </header>

        <dl class="slip__facts">
          <div><dt>Employee</dt><dd>{{ d.employee.name }} ({{ d.employee.code }})</dd></div>
          <div><dt>Designation</dt><dd>{{ d.employee.designation ?? '—' }}</dd></div>
          <div><dt>Department</dt><dd>{{ d.employee.department ?? '—' }}</dd></div>
          <div><dt>Date of joining</dt><dd>{{ d.employee.joiningDate ? (d.employee.joiningDate | date: 'd MMM y') : '—' }}</dd></div>
          <div><dt>PAN</dt><dd>{{ d.employee.pan ?? '—' }}</dd></div>
          <div><dt>UAN</dt><dd>{{ d.employee.uan ?? '—' }}</dd></div>
          <div><dt>Bank</dt><dd>{{ d.employee.bankName ?? '—' }} {{ d.employee.bankAccount ?? '' }}</dd></div>
          <div><dt>Days paid</dt><dd>{{ d.paidDays | number: '1.0-1' }} of {{ d.periodDays | number: '1.0-0' }}{{ d.lopDays ? ' (' + d.lopDays + ' LOP)' : '' }}</dd></div>
        </dl>

        <div class="slip__columns">
          <table class="slip__table">
            <thead>
              <tr><th scope="col">Earnings</th><th scope="col" class="slip__num">Amount</th></tr>
            </thead>
            <tbody>
              @for (line of d.earnings; track line.code) {
                <tr><td>{{ line.name }}@if (line.note) {<span class="muted slip__small"> · {{ line.note }}</span>}</td><td class="slip__num">{{ line.amount | gggsMoney: d.currency }}</td></tr>
              }
              @for (line of d.reimbursements; track line.code) {
                <tr><td>{{ line.name }}</td><td class="slip__num">{{ line.amount | gggsMoney: d.currency }}</td></tr>
              }
            </tbody>
            <tfoot>
              <tr><th scope="row">Gross</th><td class="slip__num">{{ d.gross | gggsMoney: d.currency }}</td></tr>
            </tfoot>
          </table>
          <table class="slip__table">
            <thead>
              <tr><th scope="col">Deductions</th><th scope="col" class="slip__num">Amount</th></tr>
            </thead>
            <tbody>
              @for (line of d.deductions; track line.code) {
                <tr><td>{{ line.name }}</td><td class="slip__num">{{ line.amount | gggsMoney: d.currency }}</td></tr>
              } @empty {
                <tr><td class="muted" colspan="2">No deductions</td></tr>
              }
            </tbody>
            <tfoot>
              <tr><th scope="row">Total deductions</th><td class="slip__num">{{ d.totalDeductions | gggsMoney: d.currency }}</td></tr>
            </tfoot>
          </table>
        </div>

        <div class="slip__net">
          <span>Net pay</span>
          <strong>{{ d.net | gggsMoney: d.currency }}</strong>
          <span class="muted slip__small">{{ d.netInWords }}</span>
        </div>

        @if (d.employerContributions.length) {
          <p class="slip__small muted">
            Paid by the employer on top:
            @for (line of d.employerContributions; track line.code; let last = $last) {
              {{ line.name }} {{ line.amount | gggsMoney: d.currency }}{{ last ? '' : ' · ' }}
            }
          </p>
        }

        <div class="slip__columns slip__small">
          <dl class="slip__facts slip__facts--tight">
            <div><dt>Gross, year to date</dt><dd>{{ d.yearToDate.gross | gggsMoney: d.currency }}</dd></div>
            <div><dt>TDS, year to date</dt><dd>{{ d.yearToDate.tds | gggsMoney: d.currency }}</dd></div>
            <div><dt>PF, year to date</dt><dd>{{ d.yearToDate.providentFund | gggsMoney: d.currency }}</dd></div>
          </dl>
          @if (d.tax; as tax) {
            <dl class="slip__facts slip__facts--tight">
              <div><dt>Tax regime</dt><dd>{{ tax.regime }}</dd></div>
              <div><dt>Projected taxable income</dt><dd>{{ tax.taxableIncome | gggsMoney: d.currency }}</dd></div>
              <div><dt>Projected tax for the year</dt><dd>{{ tax.annualTax | gggsMoney: d.currency }}</dd></div>
            </dl>
          }
        </div>
        <p class="muted slip__small">Computer-generated payslip; no signature is required.</p>
      </article>
    }
  `,
  styleUrl: './money-ui.css',
})
export class PayslipDocumentView {
  readonly document = input<PayslipDocument | null>(null);
}

/** Employee 360 payroll tab: the salary in force, laid out, its history, gratuity earned, and payslips. */
@Component({
  selector: 'gggs-employee-payroll',
  imports: [DatePipe, DecimalPipe, RouterLink, MoneyPipe, KpiCard, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (salary(); as s) {
      @if (s.current; as current) {
        <div class="stack">
          <div class="money-kpis">
            <gggs-kpi-card label="Annual CTC" [value]="current.annualCtc | gggsMoney: current.currency" [hint]="current.structureName" />
            <gggs-kpi-card label="Monthly gross" [value]="s.breakdown?.monthlyGross ?? 0 | gggsMoney" />
            <gggs-kpi-card label="Take-home (estimate)" [value]="s.breakdown?.monthlyTakeHome ?? 0 | gggsMoney" />
            @if (s.gratuity; as gratuity) {
              <gggs-kpi-card label="Gratuity earned" [value]="gratuity.amount | gggsMoney" [hint]="(gratuity.yearsOfService | number: '1.0-1') + ' yrs' + (gratuity.eligible ? '' : ' · from 5 yrs')" />
            }
          </div>
          <table class="money-table">
            <thead><tr><th scope="col">Component</th><th scope="col" class="money-num">Monthly</th><th scope="col" class="money-num">Annual</th></tr></thead>
            <tbody>
              @for (line of s.breakdown?.lines ?? []; track line.code) {
                <tr [class.muted]="line.kind !== 'Earning'"><td>{{ line.name }}</td><td class="money-num">{{ line.monthly | gggsMoney }}</td><td class="money-num">{{ line.annual | gggsMoney }}</td></tr>
              }
            </tbody>
          </table>
          <h3 class="money-heading">Salary history</h3>
          @for (row of s.history; track row.id) {
            <div class="money-row">
              <strong>{{ row.annualCtc | gggsMoney: row.currency }}</strong>
              <span>{{ humanize(row.reason) }}{{ row.changePercent !== undefined ? ' · ' + (row.changePercent > 0 ? '+' : '') + row.changePercent + '%' : '' }}</span>
              <span class="muted">{{ row.effectiveFrom | date: 'd MMM y' }} – {{ row.effectiveTo ? (row.effectiveTo | date: 'd MMM y') : 'now' }}</span>
              <span class="muted">{{ row.structureName }} · {{ row.source }}</span>
            </div>
          }
          <h3 class="money-heading">Payslips</h3>
          @for (payslip of payslips(); track payslip.id) {
            <a class="money-row" [routerLink]="['/payroll/payslips', payslip.id]">
              <strong>{{ payslip.periodLabel }}</strong>
              <span>Net {{ payslip.net | gggsMoney: payslip.currency }}</span>
              <span class="muted">Gross {{ payslip.gross | gggsMoney: payslip.currency }}</span>
              <span class="muted">Paid {{ payslip.payDate | date: 'd MMM y' }}</span>
            </a>
          } @empty {
            <p class="muted">No payslips yet.</p>
          }
        </div>
      } @else {
        <gggs-empty-state icon="pi pi-wallet" title="No salary assigned" description="Assign a salary in Payroll > Salaries before the first payroll." />
      }
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './money-ui.css',
})
export class EmployeePayroll {
  private readonly api = inject(PayrollApi);

  readonly employeeId = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly salary = signal<EmployeeSalaryView | null>(null);
  protected readonly payslips = signal<readonly PayslipItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.employeeId();
      Promise.all([this.api.salary(id), this.api.employeePayslips(id)]).then(
        ([salary, payslips]) => {
          this.salary.set(salary);
          this.payslips.set(payslips);
        },
        (error: unknown) => this.error.set(message(error, 'Payroll could not be loaded.')),
      );
    });
  }
}

/** Total rewards (spec §23): salary, band position, variable pay, benefits and equity - for Employee 360 and for oneself. */
@Component({
  selector: 'gggs-total-rewards',
  imports: [DatePipe, DecimalPipe, MoneyPipe, KpiCard, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rewards(); as r) {
      <div class="stack">
        <div class="money-kpis">
          <gggs-kpi-card label="Total annual rewards" [value]="r.totalAnnualRewards | gggsMoney: r.currency" icon="pi pi-gift" />
          <gggs-kpi-card label="Annual CTC" [value]="r.annualCtc | gggsMoney: r.currency" />
          <gggs-kpi-card label="Variable pay (12 months)" [value]="r.variablePayLast12Months | gggsMoney: r.currency" />
          <gggs-kpi-card label="Benefits paid by employer" [value]="r.benefitsEmployerAnnual | gggsMoney: r.currency" hint="a year" />
          @if (r.grants.length) {
            <gggs-kpi-card label="Vested equity value" [value]="r.equityVestedValue | gggsMoney: r.currency" />
          }
        </div>

        @if (r.band; as band) {
          <section class="stack">
            <h3 class="money-heading">Band position{{ band.gradeName ? ' · ' + band.gradeName : '' }}</h3>
            <div class="band" [attr.aria-label]="'Compa-ratio ' + band.compaRatio">
              <div class="band__track">
                <span class="band__marker" [style.left.%]="clamp(band.penetration)"></span>
                <span class="band__mid"></span>
              </div>
              <div class="band__labels muted">
                <span>{{ band.minimum | gggsMoney: r.currency }}</span>
                <span>{{ band.midpoint | gggsMoney: r.currency }}</span>
                <span>{{ band.maximum | gggsMoney: r.currency }}</span>
              </div>
            </div>
            <p>Compa-ratio <strong>{{ band.compaRatio | number: '1.2-2' }}</strong> · {{ band.position === 'Within' ? 'within the band' : band.position === 'Below' ? 'below the band minimum' : 'above the band maximum' }}</p>
          </section>
        }

        @if (r.salaryLines.length) {
          <table class="money-table">
            <thead><tr><th scope="col">Salary component</th><th scope="col" class="money-num">Monthly</th><th scope="col" class="money-num">Annual</th></tr></thead>
            <tbody>
              @for (line of r.salaryLines; track line.code) {
                <tr><td>{{ line.name }}</td><td class="money-num">{{ line.monthly | gggsMoney: r.currency }}</td><td class="money-num">{{ line.annual | gggsMoney: r.currency }}</td></tr>
              }
            </tbody>
          </table>
        }

        @if (r.revisions.length) {
          <h3 class="money-heading">Revisions</h3>
          @for (revision of r.revisions; track revision.id) {
            <div class="money-row">
              <strong>{{ revision.proposedCtc | gggsMoney: revision.currency }}</strong>
              <span>{{ humanize(revision.type) }} · {{ revision.increasePercent > 0 ? '+' : '' }}{{ revision.increasePercent }}%</span>
              <span class="muted">from {{ revision.effectiveDate | date: 'd MMM y' }}</span>
              <span class="muted">{{ revision.newDesignation ?? '' }}</span>
            </div>
          }
        }

        @if (r.awards.length) {
          <h3 class="money-heading">Variable pay</h3>
          @for (award of r.awards; track award.id) {
            <div class="money-row">
              <strong>{{ award.amount | gggsMoney: award.currency }}</strong>
              <span>{{ humanize(award.type) }}</span>
              <span class="muted">{{ award.payoutMonth | date: 'MMM y' }} · {{ award.reason }}</span>
              <gggs-status-badge [status]="award.status" />
            </div>
          }
        }

        @if (r.benefits.length) {
          <h3 class="money-heading">Benefits this month</h3>
          @for (charge of r.benefits; track charge.enrollmentId) {
            <div class="money-row">
              <strong>{{ charge.planName }}</strong>
              <span>Employer {{ charge.employerAmount | gggsMoney: r.currency }}</span>
              <span class="muted">You {{ charge.employeeAmount | gggsMoney: r.currency }}</span>
              <span></span>
            </div>
          }
        }

        @if (r.grants.length) {
          <h3 class="money-heading">Equity</h3>
          @for (grant of r.grants; track grant.id) {
            <div class="money-row">
              <strong>{{ grant.planName }}</strong>
              <span>{{ grant.vestedUnits | number: '1.0-2' }} of {{ grant.units | number: '1.0-2' }} vested</span>
              <span class="muted">granted {{ grant.grantDate | date: 'MMM y' }}{{ grant.nextVestingDate ? ' · next ' + (grant.nextVestingDate | date: 'd MMM y') : '' }}</span>
              <gggs-status-badge [status]="grant.status" />
            </div>
          }
        }
      </div>
    }
  `,
  styleUrl: './money-ui.css',
})
export class TotalRewardsView {
  readonly rewards = input<TotalRewards | null>(null);

  protected readonly humanize = humanize;

  protected clamp(value: number): number {
    return Math.max(0, Math.min(100, value));
  }
}

/** Employee 360 compensation tab. */
@Component({
  selector: 'gggs-employee-compensation',
  imports: [TotalRewardsView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-total-rewards [rewards]="rewards()" />
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
})
export class EmployeeCompensation {
  private readonly api = inject(CompensationApi);

  readonly employeeId = input.required<string>();

  protected readonly rewards = signal<TotalRewards | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.rewards(this.employeeId()).then(
        (rewards) => this.rewards.set(rewards),
        (error: unknown) => this.error.set(message(error, 'Compensation could not be loaded.')),
      );
    });
  }
}

/** Employee 360 benefits tab: plans, cover and the monthly cost of each. */
@Component({
  selector: 'gggs-employee-benefits',
  imports: [DatePipe, MoneyPipe, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (enrollment of enrollments(); track enrollment.id) {
        <div class="money-row">
          <strong>{{ enrollment.planName }}</strong>
          <span>You {{ enrollment.employeeMonthly | gggsMoney }} · employer {{ enrollment.employerMonthly | gggsMoney }} a month</span>
          <span class="muted">{{ enrollment.effectiveFrom | date: 'd MMM y' }}{{ enrollment.effectiveTo ? ' – ' + (enrollment.effectiveTo | date: 'd MMM y') : '' }}{{ enrollment.dependents.length ? ' · ' + enrollment.dependents.length + ' dependent(s)' : '' }}</span>
          <gggs-status-badge [status]="enrollment.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-heart" title="Not enrolled in any plan" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>
  `,
  styleUrl: './money-ui.css',
})
export class EmployeeBenefits {
  private readonly api = inject(BenefitsApi);

  readonly employeeId = input.required<string>();

  protected readonly enrollments = signal<readonly EnrollmentItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.forEmployee(this.employeeId()).then(
        (enrollments) => this.enrollments.set(enrollments),
        (error: unknown) => this.error.set(message(error, 'Benefits could not be loaded.')),
      );
    });
  }
}
