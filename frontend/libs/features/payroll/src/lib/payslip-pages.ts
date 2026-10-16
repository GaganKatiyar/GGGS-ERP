import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { PayslipItem, PayslipView, TaxStatement } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi, PayslipDocumentView } from '@gggs/shared/money';
import { EmptyState, PageHeader, Skeleton, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';
import { financialYearLabel, financialYearOf, message, recentFinancialYears, thisMonth } from './payroll-shared';

/** Published payslips for a month, for payroll to look up and reprint. */
@Component({
  selector: 'gggs-payslips-page',
  imports: [RouterLink, MoneyPipe, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payslips" subtitle="Payslips published by finalized payroll runs." />
    <section class="panel toolbar pay__filters">
      <label class="field">
        <span class="field__label">Month</span>
        <input type="month" [value]="period().slice(0, 7)" (change)="setPeriod($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" />
      </label>
    </section>
    <section class="panel stack">
      @for (payslip of filtered(); track payslip.id) {
        <a class="pay__row" [routerLink]="['/payroll/payslips', payslip.id]">
          <span><strong>{{ payslip.employeeName ?? payslip.employeeCode }}</strong> <span class="muted">{{ payslip.employeeCode }}</span></span>
          <span>{{ payslip.periodLabel }}</span>
          <span class="muted">Gross {{ payslip.gross | gggsMoney: payslip.currency }}</span>
          <span><strong>Net {{ payslip.net | gggsMoney: payslip.currency }}</strong></span>
          <span></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-file" title="No payslips for this month" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './payroll.css',
})
export class PayslipsPage {
  private readonly api = inject(PayrollApi);

  protected readonly period = signal(thisMonth());
  protected readonly search = signal('');
  protected readonly payslips = signal<readonly PayslipItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected filtered(): readonly PayslipItem[] {
    const term = this.search().trim().toLowerCase();
    return term
      ? this.payslips().filter((payslip) => (payslip.employeeName ?? '').toLowerCase().includes(term) || (payslip.employeeCode ?? '').toLowerCase().includes(term))
      : this.payslips();
  }

  protected setPeriod(month: string): void {
    if (month) {
      this.period.set(`${month}-01`);
      void this.load();
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.payslips.set(await this.api.payslips(this.period()));
    } catch (error) {
      this.error.set(message(error, 'Payslips could not be loaded.'));
    }
  }
}

/** One payslip, to read and print. Used for anyone's (payroll) and for one's own. */
@Component({
  selector: 'gggs-payslip-page',
  imports: [PageHeader, Skeleton, PayslipDocumentView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header [title]="payslip() ? 'Payslip · ' + payslip()?.document?.periodLabel : 'Payslip'">
      <button type="button" class="btn pay__no-print" (click)="print()"><i class="pi pi-print" aria-hidden="true"></i> Print</button>
    </gggs-page-header>
    @if (payslip(); as p) {
      <gggs-payslip-document [document]="p.document" />
    } @else if (!error()) {
      <gggs-skeleton [lines]="12" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './payroll.css',
})
export class PayslipPage {
  private readonly api = inject(PayrollApi);

  readonly id = input.required<string>();

  /** From the route: one's own payslip rather than anyone's. */
  readonly mine = input(false);

  protected readonly payslip = signal<PayslipView | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.id();
      (this.mine() ? this.api.myPayslip(id) : this.api.payslip(id)).then(
        (payslip) => this.payslip.set(payslip),
        (error: unknown) => this.error.set(message(error, 'The payslip could not be loaded.')),
      );
    });
  }

  protected print(): void {
    window.print();
  }
}

/** The year's salary and tax (Form 16 Part B), for oneself. */
@Component({
  selector: 'gggs-tax-statement',
  imports: [MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (statement(); as s) {
      <div class="stack">
        <p class="muted">{{ s.employerName }}{{ s.employerTan ? ' · TAN ' + s.employerTan : '' }} · {{ s.employeeName }}{{ s.pan ? ' · PAN ' + s.pan : '' }} · {{ s.regime }} regime</p>
        <table class="pay__table">
          <tbody>
            <tr><td>Gross salary paid</td><td class="pay__num">{{ s.grossSalary | gggsMoney }}</td></tr>
            <tr><td>Exemptions (HRA)</td><td class="pay__num">−{{ s.exemptions | gggsMoney }}</td></tr>
            <tr><td>Standard deduction</td><td class="pay__num">−{{ s.standardDeduction | gggsMoney }}</td></tr>
            <tr><td>Professional tax</td><td class="pay__num">−{{ s.professionalTax | gggsMoney }}</td></tr>
            <tr><td>Chapter VI-A deductions</td><td class="pay__num">−{{ s.chapterVIA | gggsMoney }}</td></tr>
            <tr><td>Taxable income (projected)</td><td class="pay__num">{{ s.taxableIncome | gggsMoney }}</td></tr>
            <tr><td>Tax · rebate · cess</td><td class="pay__num">{{ s.taxOnIncome | gggsMoney }} · −{{ s.rebate | gggsMoney }} · {{ s.cess | gggsMoney }}</td></tr>
            <tr><td>Tax for the year (projected)</td><td class="pay__num">{{ s.annualTax | gggsMoney }}</td></tr>
            <tr><td><strong>TDS deducted so far</strong></td><td class="pay__num"><strong>{{ s.tdsDeducted | gggsMoney }}</strong></td></tr>
          </tbody>
        </table>
        <table class="pay__table">
          <thead><tr><th scope="col">Month</th><th scope="col" class="pay__num">Gross</th><th scope="col" class="pay__num">Taxable</th><th scope="col" class="pay__num">TDS</th></tr></thead>
          <tbody>
            @for (month of s.months; track month.period) {
              <tr><td>{{ month.periodLabel }}</td><td class="pay__num">{{ month.gross | gggsMoney }}</td><td class="pay__num">{{ month.taxable | gggsMoney }}</td><td class="pay__num">{{ month.tds | gggsMoney }}</td></tr>
            } @empty {
              <tr><td colspan="4" class="muted">No payroll in this year yet.</td></tr>
            }
          </tbody>
        </table>
        <p class="muted">This is Part B of Form 16. Part A (tax deposited) is issued from the TRACES portal after the year ends.</p>
      </div>
    }
  `,
  styleUrl: './payroll.css',
})
export class TaxStatementView {
  readonly statement = input<TaxStatement | null>(null);
}

/** My payslips and the year's tax statement (spec §16 self-service). */
@Component({
  selector: 'gggs-my-payslips-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, Tabs, EmptyState, TaxStatementView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My payslips" subtitle="Your payslips, and the tax deducted this year.">
      <a class="btn" routerLink="/me/tax-declaration">Tax declaration</a>
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
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'payslips') {
        @for (payslip of payslips(); track payslip.id) {
          <a class="pay__row" [routerLink]="['/me/payslips', payslip.id]">
            <strong>{{ payslip.periodLabel }}</strong>
            <span><strong>Net {{ payslip.net | gggsMoney: payslip.currency }}</strong></span>
            <span class="muted">Gross {{ payslip.gross | gggsMoney: payslip.currency }}</span>
            <span class="muted">Deductions {{ payslip.deductions | gggsMoney: payslip.currency }}</span>
            <span class="muted">Paid {{ payslip.payDate | date: 'd MMM' }}</span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-wallet" title="No payslips in this year" />
        }
      } @else {
        <gggs-tax-statement [statement]="statement()" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './payroll.css',
})
export class MyPayslipsPage {
  private readonly api = inject(PayrollApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'payslips', label: 'Payslips' },
    { key: 'tax', label: 'Tax statement' },
  ];
  protected readonly years = recentFinancialYears();
  protected readonly yearLabel = financialYearLabel;
  protected readonly year = signal(financialYearOf(thisMonth()));
  protected readonly tab = signal('payslips');
  protected readonly payslips = signal<readonly PayslipItem[]>([]);
  protected readonly statement = signal<TaxStatement | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      const [payslips, statement] = await Promise.all([this.api.myPayslips(this.year()), this.api.myTaxStatement(this.year())]);
      this.payslips.set(payslips);
      this.statement.set(statement);
    } catch (error) {
      this.error.set(message(error, 'Your payslips could not be loaded.'));
    }
  }
}
