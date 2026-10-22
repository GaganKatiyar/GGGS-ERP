import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import type { ItemStatus, PayLine, RunDetail, RunItemDetail, RunItemRow } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi, type PayrollReport } from '@gggs/shared/money';
import { Drawer, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { message } from './payroll-shared';

/**
 * One payroll run (spec §21): readiness, calculation, the people in it with their issues, holds,
 * approval, finalization (payslips go out), payment, and the files for the bank and the authorities.
 */
@Component({
  selector: 'gggs-payroll-run-page',
  imports: [DatePipe, DecimalPipe, RouterLink, MoneyPipe, PageHeader, KpiCard, StatusBadge, EmptyState, Skeleton, Drawer, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (detail(); as d) {
      <gggs-page-header [title]="(d.run.kind === 'FullAndFinal' ? 'Full and final · ' : 'Payroll · ') + d.run.label" [subtitle]="(d.run.notes && d.run.kind === 'FullAndFinal' ? d.run.notes + ' ' : '') + 'Pay date ' + (d.run.payDate | date: 'd MMM y')">
        <a class="btn" routerLink="/payroll/runs">All runs</a>
        @if (canProcess && (d.run.status === 'Draft' || d.run.status === 'Calculated')) {
          <button type="button" class="btn" [disabled]="busy()" (click)="act('calculate')">{{ d.run.status === 'Draft' ? 'Calculate' : 'Recalculate' }}</button>
        }
        @if (canProcess && d.run.status === 'Calculated') {
          <button type="button" class="btn btn--primary" [disabled]="busy() || d.run.errors > 0" (click)="act('submit')">Send for approval</button>
        }
        @if (canFinalize && d.run.status === 'Approved') {
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('finalize')">Finalize and publish payslips</button>
        }
        @if (canFinalize && d.run.status === 'Finalized') {
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="paidOpen.set(true)">Mark paid</button>
        }
        @if (canProcess && d.run.status !== 'Finalized' && d.run.status !== 'Paid' && d.run.status !== 'Cancelled') {
          <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('cancel')">Cancel run</button>
        }
      </gggs-page-header>

      <section class="panel stack">
        <div class="toolbar">
          <gggs-status-badge [status]="d.run.status" />
          @if (d.run.status === 'PendingApproval') {
            <span class="muted">With approvers. <a routerLink="/approvals">Approvals inbox</a></span>
          }
          @if (d.run.paymentReference) {
            <span class="muted">Paid {{ d.run.paidDate | date: 'd MMM y' }} · {{ d.run.paymentReference }}</span>
          }
          <span class="pay__spacer"></span>
          @if (d.netVariancePercent !== undefined) {
            <span [class.pay__warning]="abs(d.netVariancePercent) > 10">{{ d.netVariancePercent > 0 ? '+' : '' }}{{ d.netVariancePercent }}% net pay vs {{ d.previous?.label }}</span>
          }
        </div>
        @for (check of d.readiness; track check.code) {
          <p class="pay__check" [class.pay__warning]="check.level === 'Warning'">
            <i [class]="check.level === 'Warning' ? 'pi pi-exclamation-triangle' : 'pi pi-info-circle'" aria-hidden="true"></i>
            {{ check.message }}
          </p>
        }
      </section>

      <div class="pay__kpis">
        <gggs-kpi-card label="People paid" [value]="d.run.employeeCount" [hint]="d.held ? d.held + ' on hold' : undefined" icon="pi pi-users" />
        <gggs-kpi-card label="Gross" [value]="d.run.gross | gggsMoney: d.run.currency" />
        <gggs-kpi-card label="Deductions" [value]="d.run.deductions | gggsMoney: d.run.currency" />
        <gggs-kpi-card label="Net pay" [value]="d.run.net | gggsMoney: d.run.currency" />
        <gggs-kpi-card label="Employer cost" [value]="d.run.employerCost | gggsMoney: d.run.currency" />
      </div>

      <div class="pay__two">
        <section class="panel stack">
          <h2 class="pay__heading">Statutory</h2>
          <table class="table pay__table">
            <tbody>
              <tr><td>PF - employee / employer</td><td class="pay__num">{{ d.statutory.pfEmployee | gggsMoney }} / {{ d.statutory.pfEmployer | gggsMoney }}</td></tr>
              <tr><td>ESI - employee / employer</td><td class="pay__num">{{ d.statutory.esiEmployee | gggsMoney }} / {{ d.statutory.esiEmployer | gggsMoney }}</td></tr>
              <tr><td>Professional tax</td><td class="pay__num">{{ d.statutory.professionalTax | gggsMoney }}</td></tr>
              <tr><td>TDS</td><td class="pay__num">{{ d.statutory.tds | gggsMoney }}</td></tr>
              <tr><td>Labour welfare fund</td><td class="pay__num">{{ d.statutory.labourWelfare | gggsMoney }}</td></tr>
            </tbody>
          </table>
        </section>
        @if (canReport && d.run.status !== 'Draft') {
          <section class="panel stack">
            <h2 class="pay__heading">Files</h2>
            <div class="toolbar">
              <button type="button" class="btn btn--sm" (click)="download('register')">Payroll register</button>
              <button type="button" class="btn btn--sm" (click)="download('journal')">Accounting journal</button>
              <button type="button" class="btn btn--sm" (click)="download('professional-tax')">Professional tax</button>
            </div>
            @if (d.run.status === 'Finalized' || d.run.status === 'Paid') {
              <div class="toolbar">
                <button type="button" class="btn btn--sm" (click)="download('bank-file')">Bank transfer file</button>
                <button type="button" class="btn btn--sm" (click)="download('ecr')">EPF ECR</button>
                <button type="button" class="btn btn--sm" (click)="download('epf-exits')">EPF exits</button>
                <button type="button" class="btn btn--sm" (click)="download('esi')">ESI contribution</button>
              </div>
            } @else {
              <p class="muted">The bank file and statutory returns are produced once the run is finalized.</p>
            }
          </section>
        }
      </div>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="pay__heading">People</h2>
          <span class="pay__spacer"></span>
          <label class="field">
            <span class="field__label">Show</span>
            <select [value]="filter()" (change)="filter.set($any($event.target).value); loadItems()">
              <option value="">Everyone</option>
              <option value="Error">With errors</option>
              <option value="Warning">With warnings</option>
              <option value="held">On hold</option>
            </select>
          </label>
          <label class="field">
            <span class="field__label">Search</span>
            <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" (keyup.enter)="loadItems()" (search)="loadItems()" />
          </label>
        </div>
        @for (row of items(); track row.id) {
          <button type="button" class="pay__row" (click)="open(row)">
            <span><strong>{{ row.employeeName }}</strong> <span class="muted">{{ row.employeeCode }}</span>{{ row.onHold ? ' · on hold' : '' }}</span>
            <span>{{ row.paidDays | number: '1.0-1' }} days{{ row.lopDays ? ' · ' + row.lopDays + ' LOP' : '' }}</span>
            <span>Gross {{ row.gross | gggsMoney }}</span>
            <span><strong>Net {{ row.net | gggsMoney }}</strong></span>
            <span [title]="row.firstIssue ?? ''"><gggs-status-badge [status]="row.onHold ? 'OnHold' : row.status" /></span>
          </button>
        } @empty {
          <gggs-empty-state icon="pi pi-users" [title]="d.run.status === 'Draft' ? 'Calculate the run to see everyone' : 'No one matches'" />
        }
        @if (hasMore()) {
          <button type="button" class="btn" (click)="more()">Show more</button>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="10" />
    }
    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-drawer [(visible)]="drawerOpen" [title]="item() ? item()?.employeeName + ' · ' + item()?.employeeCode : 'Employee'" width="min(44rem, 100vw)">
      @if (item(); as i) {
        <div class="stack">
          <div class="toolbar">
            <gggs-status-badge [status]="i.onHold ? 'OnHold' : i.status" />
            <span class="muted">CTC {{ i.annualCtc | gggsMoney }} · {{ i.paidDays | number: '1.0-1' }} of {{ i.periodDays | number: '1.0-0' }} days{{ i.stateCode ? ' · ' + i.stateCode : '' }}</span>
          </div>
          @for (issue of i.issues; track issue) {
            <p class="pay__check" [class.pay__danger]="i.status === 'Error'"><i class="pi pi-exclamation-circle" aria-hidden="true"></i> {{ issue }}</p>
          }
          <table class="table pay__table">
            <thead><tr><th scope="col">Line</th><th scope="col">Kind</th><th scope="col" class="pay__num">Amount</th></tr></thead>
            <tbody>
              @for (line of i.lines; track line.code) {
                <tr [class.muted]="line.kind === 'EmployerContribution'">
                  <td>{{ line.name }}@if (line.note) {<br /><span class="muted">{{ line.note }}</span>}</td>
                  <td>{{ humanize(line.kind) }}</td>
                  <td class="pay__num">{{ signed(line) | gggsMoney }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr><th scope="row" colspan="2">Net pay</th><td class="pay__num"><strong>{{ i.net | gggsMoney }}</strong></td></tr>
            </tfoot>
          </table>
          @if (i.tax; as tax) {
            <details>
              <summary>How TDS was worked out ({{ tax.regime }} regime)</summary>
              <table class="table pay__table">
                <tbody>
                  <tr><td>Projected taxable salary for the year</td><td class="pay__num">{{ tax.grossTaxableYear | gggsMoney }}</td></tr>
                  <tr><td>Standard deduction</td><td class="pay__num">−{{ tax.standardDeduction | gggsMoney }}</td></tr>
                  @if (tax.hraExemption) {
                    <tr><td>HRA exemption</td><td class="pay__num">−{{ tax.hraExemption | gggsMoney }}</td></tr>
                  }
                  @if (tax.professionalTax) {
                    <tr><td>Professional tax</td><td class="pay__num">−{{ tax.professionalTax | gggsMoney }}</td></tr>
                  }
                  @if (tax.chapterVIA) {
                    <tr><td>Chapter VI-A deductions</td><td class="pay__num">−{{ tax.chapterVIA | gggsMoney }}</td></tr>
                  }
                  <tr><td>Taxable income</td><td class="pay__num">{{ tax.taxableIncome | gggsMoney }}</td></tr>
                  <tr><td>Tax · rebate · surcharge · cess</td><td class="pay__num">{{ tax.taxBeforeRebate | gggsMoney }} · −{{ tax.rebate | gggsMoney }} · {{ tax.surcharge | gggsMoney }} · {{ tax.cess | gggsMoney }}</td></tr>
                  <tr><td>Tax for the year</td><td class="pay__num">{{ tax.annualTax | gggsMoney }}</td></tr>
                  <tr><td>Already deducted</td><td class="pay__num">{{ tax.alreadyDeducted | gggsMoney }}</td></tr>
                  <tr><td>This month (over {{ tax.monthsRemaining }} remaining)</td><td class="pay__num"><strong>{{ tax.monthlyTds | gggsMoney }}</strong></td></tr>
                </tbody>
              </table>
            </details>
          }
          @if (i.rules.length) {
            <p class="muted">
              Rules used:
              @for (rule of i.rules; track rule.ruleId; let last = $last) {
                <span>{{ rule.code }} v{{ rule.version }}{{ last ? '' : ',' }}</span>
              }
            </p>
          }
          @if (canProcess && (detail()?.run?.status === 'Calculated' || detail()?.run?.status === 'Draft')) {
            <div class="toolbar">
              @if (i.onHold) {
                <span class="muted">Held: {{ i.holdReason }}</span>
                <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="release(i)">Release</button>
              } @else {
                <button type="button" class="btn btn--sm" (click)="holdOpen.set(true)">Hold from this run</button>
              }
            </div>
          }
        </div>
      }
    </gggs-drawer>

    <gggs-form-dialog [(visible)]="holdOpen" title="Hold from this run" saveLabel="Hold" [busy]="busy()" [saveDisabled]="!reason().trim()" (save)="hold()">
      <p class="muted">A held person is not paid this month; their one-off inputs move to next month.</p>
      <label class="field">
        <span class="field__label">Reason</span>
        <input type="text" maxlength="300" [value]="reason()" (input)="reason.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="paidOpen" title="Mark the run paid" saveLabel="Mark paid" [busy]="busy()" [saveDisabled]="!paidOn()" (save)="markPaid()">
      <label class="field">
        <span class="field__label">Paid on</span>
        <input type="date" [value]="paidOn()" (change)="paidOn.set($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Bank reference</span>
        <input type="text" maxlength="100" [value]="paidReference()" (input)="paidReference.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './payroll.css',
})
export class PayrollRunPage {
  private readonly api = inject(PayrollApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canProcess = this.permissions.has('payroll.run.process');
  protected readonly canFinalize = this.permissions.has('payroll.run.finalize');
  protected readonly canReport = this.permissions.has('payroll.report.view');
  protected readonly humanize = humanize;
  protected readonly detail = signal<RunDetail | null>(null);
  protected readonly items = signal<readonly RunItemRow[]>([]);
  protected readonly item = signal<RunItemDetail | null>(null);
  protected readonly filter = signal('');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly drawerOpen = signal(false);
  protected readonly holdOpen = signal(false);
  protected readonly paidOpen = signal(false);
  protected readonly reason = signal('');
  protected readonly paidOn = signal(new Date().toISOString().slice(0, 10));
  protected readonly paidReference = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly status = computed<ItemStatus | ''>(() => (this.filter() === 'Error' || this.filter() === 'Warning' ? (this.filter() as ItemStatus) : ''));

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  protected abs(value: number): number {
    return Math.abs(value);
  }

  /** Deductions show as negative so the column adds up to net pay. */
  protected signed(line: PayLine): number {
    return line.kind === 'Deduction' ? -line.amount : line.amount;
  }

  protected async act(action: 'calculate' | 'submit' | 'finalize' | 'cancel'): Promise<void> {
    await this.run(async () => {
      const id = this.id();
      switch (action) {
        case 'calculate':
          this.detail.set(await this.api.calculate(id));
          this.notice.set('Calculated.');
          break;
        case 'submit': {
          const outcome = await this.api.submit(id);
          this.notice.set(outcome.status === 'Approved' ? 'Approved - no approval workflow is set up for payroll.' : 'Sent for approval.');
          break;
        }
        case 'finalize':
          this.detail.set(await this.api.finalize(id));
          this.notice.set('Finalized. Payslips are published and employees notified.');
          break;
        default:
          await this.api.cancelRun(id);
          this.notice.set('Run cancelled.');
      }
      await this.load(id);
    });
  }

  protected async markPaid(): Promise<void> {
    await this.run(async () => {
      await this.api.markPaid(this.id(), this.paidOn(), this.paidReference().trim() || null);
      this.paidOpen.set(false);
      await this.load(this.id());
    });
  }

  protected async download(report: PayrollReport): Promise<void> {
    await this.run(() => this.api.report(this.id(), report));
  }

  protected async open(row: RunItemRow): Promise<void> {
    this.item.set(null);
    this.drawerOpen.set(true);
    try {
      this.item.set(await this.api.item(this.id(), row.id));
    } catch (error) {
      this.error.set(message(error, 'The result could not be loaded.'));
    }
  }

  protected async hold(): Promise<void> {
    const current = this.item();
    if (!current) {
      return;
    }

    await this.run(async () => {
      this.item.set(await this.api.hold(this.id(), current.id, this.reason().trim()));
      this.holdOpen.set(false);
      this.reason.set('');
      await this.load(this.id());
    });
  }

  protected async release(current: RunItemDetail): Promise<void> {
    await this.run(async () => {
      this.item.set(await this.api.release(this.id(), current.id));
      await this.load(this.id());
    });
  }

  protected async loadItems(append = false): Promise<void> {
    if (!append) {
      this.page.set(1);
    }

    const result = await this.api.items(this.id(), {
      status: this.status(),
      onHold: this.filter() === 'held' ? true : undefined,
      search: this.search(),
      page: this.page(),
      pageSize: 50,
    });
    this.items.set(append ? [...this.items(), ...result.items] : result.items);
    this.hasMore.set(result.hasNextPage ?? result.items.length === 50);
  }

  protected more(): void {
    this.page.update((page) => page + 1);
    void this.loadItems(true);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      this.detail.set(await this.api.run(id));
      await this.loadItems();
    } catch (error) {
      this.error.set(message(error, 'The run could not be loaded.'));
    }
  }
}
