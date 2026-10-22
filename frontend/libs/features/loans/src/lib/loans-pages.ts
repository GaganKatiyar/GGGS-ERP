import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LoansApi, MoneyPipe } from '@gggs/shared/money';
import type { DisbursementMode, LoanItem, LoanOffer, LoanQuote, LoanStatus, LoanSummary, LoanTypeDetails, LoanTypeItem, LoanView, MyLoans } from '@gggs/shared/models';
import { LOAN_STATUSES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);
const num = (value: string): number | null => (value === '' ? null : +value);

/** The month a loan's instalment falls in, as payroll labels it. */
const month = (iso: string | null | undefined) => (iso ? new Date(`${iso}T00:00:00`).toLocaleDateString('en-IN', { month: 'short', year: 'numeric' }) : '—');

interface ApplyForm {
  typeId: string;
  amount: number;
  tenureMonths: number;
  purpose: string;
  employeeId: string | null;
}

const blankApply = (typeId = ''): ApplyForm => ({ typeId, amount: 0, tenureMonths: 12, purpose: '', employeeId: null });

/**
 * The application form shared by "My loans" and HR's "New loan": the type, amount and tenure, and a
 * live quote of the instalment, the interest and whether the rules allow it.
 */
@Component({
  selector: 'gggs-loan-apply-form',
  imports: [FormsModule, EmployeeSelect, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="xp__grid">
      @if (forEmployee()) {
        <label class="field xp__wide">
          <span class="field__label">Employee</span>
          <gggs-employee-select ariaLabel="Employee" [ngModel]="form().employeeId" (ngModelChange)="patch({ employeeId: $event })" />
        </label>
      }
      <label class="field">
        <span class="field__label">Type</span>
        <select (change)="patch({ typeId: $any($event.target).value })">
          @for (type of types(); track type.id) {
            <option [value]="type.id" [selected]="type.id === form().typeId">{{ type.name }} · {{ type.interestRate ? type.interestRate + '% a year' : 'interest-free' }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Amount</span>
        <input type="number" min="0" step="1000" [value]="form().amount || ''" (input)="patch({ amount: +$any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Repay over (months)</span>
        <input type="number" min="1" [max]="selected()?.maxTenureMonths ?? 120" [value]="form().tenureMonths" (input)="patch({ tenureMonths: +$any($event.target).value })" />
      </label>
      <label class="field xp__wide">
        <span class="field__label">Purpose</span>
        <input type="text" maxlength="500" placeholder="What the money is for" [value]="form().purpose" (input)="patch({ purpose: $any($event.target).value })" />
      </label>
    </div>
    @if (quote(); as q) {
      <div class="xp__kpis">
        <div class="xp__card"><span class="muted">Monthly instalment</span><strong class="xp__big">{{ q.emi | gggsMoney }}</strong></div>
        <div class="xp__card"><span class="muted">Total interest</span><strong>{{ q.totalInterest | gggsMoney }}</strong></div>
        <div class="xp__card"><span class="muted">Most you can borrow</span><strong>{{ q.limit | gggsMoney }}</strong></div>
      </div>
      @if (q.problem) {
        <p class="alert alert--warning" role="status"><i class="pi pi-info-circle" aria-hidden="true"></i><span>{{ q.problem }}</span></p>
      }
    }
  `,
})
export class LoanApplyForm {
  private readonly api = inject(LoansApi);

  readonly types = input.required<readonly LoanTypeItem[]>();
  readonly form = input.required<ApplyForm>();
  readonly forEmployee = input(false);
  readonly changed = input.required<(change: Partial<ApplyForm>) => void>();

  protected readonly quote = signal<LoanQuote | null>(null);
  protected readonly selected = computed(() => this.types().find((type) => type.id === this.form().typeId) ?? null);

  constructor() {
    effect((onCleanup) => {
      const form = this.form();
      if (!form.typeId || form.amount <= 0 || form.tenureMonths <= 0 || this.forEmployee()) {
        this.quote.set(null);
        return;
      }

      const timer = setTimeout(() => {
        this.api.quote({ typeId: form.typeId, amount: form.amount, tenureMonths: form.tenureMonths }).then((quote) => this.quote.set(quote), () => this.quote.set(null));
      }, 300);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected patch(change: Partial<ApplyForm>): void {
    this.changed()(change);
  }
}

/** One's own loans and advances, what is still owed, and the loans one could apply for. */
@Component({
  selector: 'gggs-my-loans-page',
  imports: [RouterLink, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, LoanApplyForm, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My loans" subtitle="Salary advances and loans from the company, repaid in monthly instalments from your salary.">
      <button type="button" class="btn btn--primary" [disabled]="!mine()?.offers?.length" (click)="openApply(null)">Apply for a loan</button>
    </gggs-page-header>
    @if (mine(); as m) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Still to repay" [value]="m.outstanding | gggsMoney" />
        <gggs-kpi-card label="Deducted each month" [value]="m.monthlyEmi | gggsMoney" />
        <gggs-kpi-card label="Loans open" [value]="openCount()" />
      </div>
      <section class="panel stack">
        <h2 class="xp__heading">Your loans</h2>
        @for (loan of m.loans; track loan.id) {
          <a class="xp__row" [routerLink]="['/loans', loan.id]">
            <span><strong>{{ loan.typeName }}</strong> <span class="muted">{{ loan.number }}</span></span>
            <span>{{ loan.principal | gggsMoney }} over {{ loan.tenureMonths }} months</span>
            <span>{{ loan.status === 'Active' ? (loan.outstanding | gggsMoney) + ' left' : '' }}</span>
            <span class="muted">{{ loan.nextInstalmentMonth ? 'next ' + (loan.nextInstalmentAmount | gggsMoney) + ' in ' + month(loan.nextInstalmentMonth) : '' }}</span>
            <gggs-status-badge [status]="loan.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-money-bill" title="No loans" description="Apply for a salary advance or a loan below." />
        }
      </section>
      <section class="panel stack">
        <h2 class="xp__heading">What you can apply for</h2>
        <div class="xp__cards">
          @for (offer of m.offers; track offer.type.id) {
            <div class="xp__card">
              <strong>{{ offer.type.name }}</strong>
              <span class="muted">{{ offer.type.description }}</span>
              <span>{{ offer.type.interestRate ? offer.type.interestRate + '% a year' : 'Interest-free' }} · up to {{ offer.type.maxTenureMonths }} months</span>
              @if (offer.problem) {
                <span class="muted">{{ offer.problem }}</span>
              } @else {
                <span>Up to <strong>{{ offer.limit | gggsMoney }}</strong></span>
                <button type="button" class="btn btn--sm" (click)="openApply(offer)">Apply</button>
              }
            </div>
          }
        </div>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="applyOpen" title="Apply for a loan" saveLabel="Apply" size="lg" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().typeId || form().amount <= 0 || !form().purpose.trim()" (save)="apply()">
      <gggs-loan-apply-form [types]="types()" [form]="form()" [changed]="patch" />
    </gggs-form-dialog>
  `,
})
export class MyLoansPage {
  private readonly api = inject(LoansApi);

  protected readonly month = month;
  protected readonly mine = signal<MyLoans | null>(null);
  protected readonly types = computed(() => (this.mine()?.offers ?? []).filter((offer) => !offer.problem).map((offer) => offer.type));
  protected readonly openCount = computed(() => (this.mine()?.loans ?? []).filter((loan) => ['PendingApproval', 'Approved', 'Active'].includes(loan.status)).length);
  protected readonly applyOpen = signal(false);
  protected readonly form = signal<ApplyForm>(blankApply());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly patch = (change: Partial<ApplyForm>) => this.form.update((form) => ({ ...form, ...change }));

  constructor() {
    this.load();
  }

  protected openApply(offer: LoanOffer | null): void {
    const type = offer?.type ?? this.types()[0];
    this.form.set({ ...blankApply(type?.id ?? ''), tenureMonths: Math.min(12, type?.maxTenureMonths ?? 12) });
    this.formError.set(null);
    this.applyOpen.set(true);
  }

  protected async apply(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const { typeId, amount, tenureMonths, purpose } = this.form();
      await this.api.apply({ typeId, amount, tenureMonths, purpose });
      this.applyOpen.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The application could not be made.'));
    } finally {
      this.busy.set(false);
    }
  }

  private load(): void {
    this.api.mine().then((mine) => this.mine.set(mine), (error: unknown) => this.error.set(message(error, 'Your loans could not be loaded.')));
  }
}

/** Loans for HR and payroll: what is waiting, what is being repaid, and the book by type. */
@Component({
  selector: 'gggs-loans-page',
  imports: [FormsModule, RouterLink, KeyValuePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, LoanApplyForm, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Loans and advances" subtitle="Applications approved through the workflow, paid out, and recovered through payroll.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNew()">New loan for an employee</button>
      }
    </gggs-page-header>
    @if (summary(); as s) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Awaiting approval" [value]="s.pendingApproval" />
        <gggs-kpi-card label="Approved, to pay out" [value]="s.toDisburse" />
        <gggs-kpi-card label="Being repaid" [value]="s.active" [hint]="(s.outstanding | gggsMoney) + ' outstanding'" />
        <gggs-kpi-card label="Due this month" [value]="s.dueThisMonth | gggsMoney" [hint]="(s.recoveredThisMonth | gggsMoney) + ' recovered'" />
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <section class="panel stack">
      <div class="toolbar xp__filters">
        <label class="field">
          <span class="field__label">Status</span>
          <select (change)="status.set($any($event.target).value); load()">
            <option value="">Any</option>
            @for (option of statuses; track option) {
              <option [value]="option" [selected]="option === status()">{{ humanize(option) }}</option>
            }
          </select>
        </label>
      </div>
      @for (loan of loans(); track loan.id) {
        <a class="xp__row" [routerLink]="['/loans', loan.id]">
          <span><strong>{{ loan.employeeName }}</strong> <span class="muted">{{ loan.number }}</span></span>
          <span>{{ loan.typeName }} · {{ loan.principal | gggsMoney }}</span>
          <span>{{ loan.emi | gggsMoney }} × {{ loan.tenureMonths }}{{ loan.interestRate ? ' at ' + loan.interestRate + '%' : '' }}</span>
          <span class="muted">{{ loan.status === 'Active' ? (loan.outstanding | gggsMoney) + ' outstanding' : loan.status === 'Approved' ? 'to pay out' : '' }}</span>
          <gggs-status-badge [status]="loan.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-money-bill" title="No loans" />
      }
      @if (summary(); as s) {
        <h2 class="xp__heading">Outstanding by type</h2>
        @for (entry of s.outstandingByType | keyvalue; track entry.key) {
          <div class="xp__flag"><strong>{{ entry.value | gggsMoney }}</strong> {{ entry.key }}</div>
        } @empty {
          <p class="muted">Nothing is being repaid.</p>
        }
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="newOpen" title="New loan for an employee" saveLabel="Apply" size="lg" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().employeeId || !form().typeId || form().amount <= 0 || !form().purpose.trim()" (save)="create()">
      <gggs-loan-apply-form [types]="types()" [form]="form()" [forEmployee]="true" [changed]="patch" />
      <p class="muted">The application goes through the same approval and eligibility checks as one the employee makes.</p>
    </gggs-form-dialog>
  `,
})
export class LoansPage {
  private readonly api = inject(LoansApi);

  protected readonly canManage = inject(PermissionStore).has('loan.loan.manage');
  protected readonly humanize = humanize;
  protected readonly statuses = LOAN_STATUSES;
  protected readonly summary = signal<LoanSummary | null>(null);
  protected readonly loans = signal<readonly LoanItem[]>([]);
  protected readonly types = signal<readonly LoanTypeItem[]>([]);
  protected readonly status = signal<LoanStatus | ''>('');
  protected readonly newOpen = signal(false);
  protected readonly form = signal<ApplyForm>(blankApply());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly patch = (change: Partial<ApplyForm>) => this.form.update((form) => ({ ...form, ...change }));

  constructor() {
    this.api.summary().then((summary) => this.summary.set(summary), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    this.api.types().then((types) => this.types.set(types.filter((type) => type.isActive)), () => undefined);
    this.load();
  }

  protected load(): void {
    this.api.list({ status: this.status() || null }).then((loans) => this.loans.set(loans), (error: unknown) => this.error.set(message(error, 'Loans could not be loaded.')));
  }

  protected openNew(): void {
    this.form.set(blankApply(this.types()[0]?.id ?? ''));
    this.formError.set(null);
    this.newOpen.set(true);
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.applyFor(this.form());
      this.newOpen.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The loan could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One loan: its terms, the repayment schedule, and the steps payroll and finance take on it. */
@Component({
  selector: 'gggs-loan-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, FormDialog, Skeleton, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.loan.typeName + ' · ' + v.loan.number" [subtitle]="(v.loan.employeeName ?? '') + (v.loan.employeeCode ? ' (' + v.loan.employeeCode + ')' : '')">
        <a class="btn" [routerLink]="canView ? '/loans' : '/me/loans'">{{ canView ? 'All loans' : 'My loans' }}</a>
        @if (v.loan.canCancel || (canManage && (v.loan.status === 'PendingApproval' || v.loan.status === 'Approved'))) {
          <button type="button" class="btn" [disabled]="busy()" (click)="cancel()">Cancel</button>
        }
        @if (canManage && v.loan.status === 'Approved') {
          <button type="button" class="btn btn--primary" (click)="openDisburse()">Pay out</button>
        }
        @if (canManage && v.loan.status === 'Active') {
          <button type="button" class="btn" (click)="openPrepay(v.loan.outstanding)">Record prepayment</button>
          <button type="button" class="btn btn--danger" (click)="reason.set(''); writeOffOpen.set(true)">Write off</button>
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.loan.status" /></div>
        <div><span class="muted">Amount</span><br /><strong>{{ v.loan.principal | gggsMoney }}</strong></div>
        <div><span class="muted">Interest</span><br />{{ v.loan.interestRate ? v.loan.interestRate + '% a year, reducing balance' : 'Interest-free' }}</div>
        <div><span class="muted">Instalment</span><br />{{ v.loan.emi | gggsMoney }} × {{ v.loan.tenureMonths }} months</div>
        <div><span class="muted">Outstanding</span><br /><strong>{{ v.loan.disbursedOn ? (v.loan.outstanding | gggsMoney) : '—' }}</strong></div>
        <div><span class="muted">Applied</span><br />{{ v.loan.appliedOn | date: 'd MMM y' }}</div>
        <div><span class="muted">Paid out</span><br />{{ v.loan.disbursedOn ? (v.loan.disbursedOn | date: 'd MMM y') + ' · ' + (v.loan.disbursementMode === 'Payroll' ? 'with salary' : 'bank transfer') : '—' }}{{ v.loan.disbursementReference ? ' · ' + v.loan.disbursementReference : '' }}</div>
        <div><span class="muted">Purpose</span><br />{{ v.loan.purpose }}</div>
        @if (v.loan.settlementId) {
          <div><span class="muted">Recovery</span><br />In the full and final settlement</div>
        }
        @if (v.loan.closedOn) {
          <div><span class="muted">Closed</span><br />{{ v.loan.closedOn | date: 'd MMM y' }}{{ v.loan.closeNote ? ' · ' + v.loan.closeNote : '' }}</div>
        }
        @if (v.loan.decisionNote) {
          <div><span class="muted">Decision</span><br />{{ v.loan.decisionNote }}</div>
        }
      </section>
      @if (v.schedule.length) {
        <section class="panel stack">
          <h2 class="xp__heading">Repayment schedule</h2>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>#</th><th>Month</th><th class="num">Principal</th><th class="num">Interest</th><th class="num">Instalment</th><th>Status</th></tr></thead>
              <tbody>
                @for (row of v.schedule; track row.id) {
                  <tr>
                    <td>{{ row.sequence }}</td>
                    <td>{{ month(row.month) }}</td>
                    <td class="num">{{ row.principal | gggsMoney }}</td>
                    <td class="num">{{ row.interest | gggsMoney }}</td>
                    <td class="num"><strong>{{ row.amount | gggsMoney }}</strong></td>
                    <td><gggs-status-badge [status]="row.status" />{{ row.status === 'Scheduled' && row.raised ? ' sent to payroll' : '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
      @if (v.repayments.length) {
        <section class="panel stack">
          <h2 class="xp__heading">Other repayments</h2>
          @for (row of v.repayments; track row.id) {
            <div class="xp__row">
              <span><strong>{{ row.kind === 'WriteOff' ? 'Written off' : row.kind === 'Settlement' ? 'Full and final settlement' : 'Prepayment' }}</strong></span>
              <span>{{ row.amount | gggsMoney }}</span>
              <span>{{ row.date | date: 'd MMM y' }}</span>
              <span class="muted">{{ row.reference ?? '' }}{{ row.note ? ' · ' + row.note : '' }}</span>
            </div>
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="disburseOpen" title="Pay out the loan" saveLabel="Pay out" [busy]="busy()" [error]="formError()" [saveDisabled]="mode() === 'Bank' && !reference().trim()" (save)="disburse()">
      <div class="stack">
        <label class="field">
          <span class="field__label">How</span>
          <select (change)="mode.set($any($event.target).value)">
            <option value="Payroll" [selected]="mode() === 'Payroll'">With the next salary</option>
            <option value="Bank" [selected]="mode() === 'Bank'">Bank transfer already made</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Paid on</span>
          <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">First instalment month (default: the month after)</span>
          <input type="month" [value]="firstMonth()" (change)="firstMonth.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">{{ mode() === 'Bank' ? 'Bank reference (UTR)' : 'Reference (optional)' }}</span>
          <input type="text" maxlength="100" [value]="reference()" (input)="reference.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="prepayOpen" title="Record a prepayment" saveLabel="Record" [busy]="busy()" [error]="formError()" [saveDisabled]="!amount() || amount()! <= 0" (save)="prepay()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Amount received</span>
          <input type="number" min="1" [value]="amount() ?? ''" (input)="amount.set(num($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Received on</span>
          <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Reference</span>
          <input type="text" maxlength="100" [value]="reference()" (input)="reference.set($any($event.target).value)" />
        </label>
        <p class="muted">What is left is spread over the remaining months; paying it all closes the loan.</p>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="writeOffOpen" title="Write the loan off" saveLabel="Write off" [busy]="busy()" [error]="formError()" [saveDisabled]="!reason().trim()" (save)="writeOff()">
      <label class="field">
        <span class="field__label">Why</span>
        <input type="text" maxlength="200" [value]="reason()" (input)="reason.set($any($event.target).value)" />
      </label>
      <p class="muted">The outstanding amount is not recovered and no more instalments are deducted.</p>
    </gggs-form-dialog>
  `,
})
export class LoanPage {
  private readonly api = inject(LoansApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canView = this.permissions.has('loan.loan.view');
  protected readonly canManage = this.permissions.has('loan.loan.manage');
  protected readonly month = month;
  protected readonly num = num;
  protected readonly view = signal<LoanView | null>(null);
  protected readonly disburseOpen = signal(false);
  protected readonly prepayOpen = signal(false);
  protected readonly writeOffOpen = signal(false);
  protected readonly mode = signal<DisbursementMode>('Payroll');
  protected readonly date = signal(today());
  protected readonly firstMonth = signal('');
  protected readonly reference = signal('');
  protected readonly amount = signal<number | null>(null);
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.get(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The loan could not be loaded.')),
      );
    });
  }

  protected openDisburse(): void {
    this.mode.set('Payroll');
    this.date.set(today());
    this.firstMonth.set('');
    this.reference.set('');
    this.formError.set(null);
    this.disburseOpen.set(true);
  }

  protected openPrepay(outstanding: number): void {
    this.amount.set(outstanding);
    this.date.set(today());
    this.reference.set('');
    this.formError.set(null);
    this.prepayOpen.set(true);
  }

  protected disburse(): Promise<void> {
    const first = this.firstMonth();
    return this.act(
      () => this.api.disburse(this.id(), { mode: this.mode(), reference: this.reference() || null, disbursedOn: this.date() || null, firstEmiMonth: first ? `${first}-01` : null }),
      () => this.disburseOpen.set(false),
      'The loan could not be paid out.',
    );
  }

  protected prepay(): Promise<void> {
    return this.act(
      () => this.api.prepay(this.id(), { amount: this.amount() ?? 0, date: this.date() || null, reference: this.reference() || null }),
      () => this.prepayOpen.set(false),
      'The prepayment could not be recorded.',
    );
  }

  protected writeOff(): Promise<void> {
    return this.act(() => this.api.writeOff(this.id(), this.reason()), () => this.writeOffOpen.set(false), 'The loan could not be written off.');
  }

  protected async cancel(): Promise<void> {
    this.busy.set(true);
    try {
      this.view.set(await this.api.cancel(this.id()));
    } catch (error) {
      this.error.set(message(error, 'The loan could not be cancelled.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async act(call: () => Promise<LoanView>, done: () => void, fallback: string): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.view.set(await call());
      done();
    } catch (error) {
      this.formError.set(message(error, fallback));
    } finally {
      this.busy.set(false);
    }
  }
}

const blankType = (): LoanTypeDetails => ({
  code: '',
  name: '',
  description: null,
  interestRate: 0,
  maxTenureMonths: 12,
  maxAmount: null,
  maxGrossMultiple: 1,
  minServiceMonths: 0,
  maxActive: 1,
  maxEmiPercentOfGross: 50,
  perquisiteExempt: false,
});

/** The loan types on offer and their rules. */
@Component({
  selector: 'gggs-loan-types-page',
  imports: [RouterLink, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Loan types" subtitle="What employees can borrow, for how long, at what interest, and who is eligible.">
      <a class="btn" routerLink="/loans">Loans</a>
      <button type="button" class="btn btn--primary" (click)="edit(null)">New loan type</button>
    </gggs-page-header>
    <section class="panel stack">
      @for (type of types(); track type.id) {
        <div class="xp__row">
          <span><strong>{{ type.name }}</strong> <span class="muted">{{ type.code }}</span></span>
          <span>{{ type.interestRate ? type.interestRate + '% a year' : 'Interest-free' }}{{ type.perquisiteExempt ? ' · perquisite-exempt' : '' }}</span>
          <span>{{ limit(type) }} · up to {{ type.maxTenureMonths }} months</span>
          <span class="muted">after {{ type.minServiceMonths }} month(s) · instalments up to {{ type.maxEmiPercentOfGross }}% of gross <button type="button" class="btn btn--link btn--sm" (click)="edit(type)">Edit</button></span>
          <gggs-status-badge [status]="type.isActive ? 'Active' : 'Inactive'" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-list" title="No loan types" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="open" [title]="editing() ? 'Edit loan type' : 'New loan type'" size="lg" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().code.trim() || !form().name.trim()" (save)="save()">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" maxlength="30" [value]="form().code" (input)="patch({ code: $any($event.target).value.toUpperCase() })" />
        </label>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="100" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field xp__wide">
          <span class="field__label">Description</span>
          <input type="text" maxlength="500" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Interest (% a year, 0 for interest-free)</span>
          <input type="number" min="0" max="30" step="0.25" [value]="form().interestRate" (input)="patch({ interestRate: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Longest tenure (months)</span>
          <input type="number" min="1" max="120" [value]="form().maxTenureMonths" (input)="patch({ maxTenureMonths: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Maximum amount (empty for none)</span>
          <input type="number" min="0" [value]="form().maxAmount ?? ''" (input)="patch({ maxAmount: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Up to this many months' gross (empty for none)</span>
          <input type="number" min="0" step="0.5" [value]="form().maxGrossMultiple ?? ''" (input)="patch({ maxGrossMultiple: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Service needed (months)</span>
          <input type="number" min="0" [value]="form().minServiceMonths" (input)="patch({ minServiceMonths: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Open at once, per person</span>
          <input type="number" min="1" [value]="form().maxActive" (input)="patch({ maxActive: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">All instalments up to (% of monthly gross)</span>
          <input type="number" min="1" max="100" [value]="form().maxEmiPercentOfGross" (input)="patch({ maxEmiPercentOfGross: +$any($event.target).value })" />
        </label>
        <label class="xp__flag"><input type="checkbox" [checked]="form().perquisiteExempt" (change)="patch({ perquisiteExempt: $any($event.target).checked })" /> Exempt from the interest perquisite (medical treatment)</label>
        <label class="xp__flag"><input type="checkbox" [checked]="active()" (change)="active.set($any($event.target).checked)" /> Offered</label>
      </div>
    </gggs-form-dialog>
  `,
})
export class LoanTypesPage {
  private readonly api = inject(LoansApi);
  private readonly money = new MoneyPipe();

  protected readonly num = num;
  protected readonly types = signal<readonly LoanTypeItem[]>([]);
  protected readonly open = signal(false);
  protected readonly editing = signal<LoanTypeItem | null>(null);
  protected readonly form = signal<LoanTypeDetails>(blankType());
  protected readonly active = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected limit(type: LoanTypeItem): string {
    const parts = [type.maxAmount ? this.money.transform(type.maxAmount) : null, type.maxGrossMultiple ? `${type.maxGrossMultiple}× monthly gross` : null].filter((part) => part !== null);
    return parts.length === 2 ? `the lower of ${parts.join(' and ')}` : `up to ${parts[0] ?? '—'}`;
  }

  protected patch(change: Partial<LoanTypeDetails>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected edit(type: LoanTypeItem | null): void {
    this.editing.set(type);
    if (type) {
      const { id: _id, isActive, ...details } = type;
      this.form.set(details);
      this.active.set(isActive);
    } else {
      this.form.set(blankType());
      this.active.set(true);
    }
    this.formError.set(null);
    this.open.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveType(this.editing()?.id ?? null, this.form(), this.active());
      this.open.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The loan type could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private load(): void {
    this.api.types().then((types) => this.types.set(types), (error: unknown) => this.error.set(message(error, 'Loan types could not be loaded.')));
  }
}
