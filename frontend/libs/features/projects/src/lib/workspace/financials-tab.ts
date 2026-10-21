import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { CostCategory, FinanceCost, FinanceInvoice, FinanceRate, ProjectFinance, ProjectRole } from '@gggs/shared/models';
import { COST_CATEGORIES, humanize } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { Chart, FormDialog, KpiCard, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);

type Dialog = 'budget' | 'rate' | 'cost' | 'invoice' | 'terms' | 'paid' | null;

/**
 * A project's financial dashboard (spec §74-77): contract value, revenue, billing, budget, cost,
 * forecast and margin - and the budget, rate card, costs and invoices behind them.
 */
@Component({
  selector: 'gggs-financials-tab',
  imports: [DatePipe, DecimalPipe, FormsModule, MoneyPipe, KpiCard, Chart, StatusBadge, Skeleton, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (finance(); as f) {
      <div class="stack">
        <div class="fin__kpis">
          <gggs-kpi-card label="Contract value" [value]="f.summary.contractValue | gggsMoney: f.currency" [hint]="humanize(f.billingType)" />
          <gggs-kpi-card label="Revenue" [value]="f.summary.revenue | gggsMoney: f.currency" [hint]="'forecast ' + (f.summary.forecastRevenue | gggsMoney: f.currency)" />
          <gggs-kpi-card label="Billed / unbilled" [value]="f.summary.billed | gggsMoney: f.currency" [hint]="(f.summary.unbilled | gggsMoney: f.currency) + ' unbilled'" />
          <gggs-kpi-card label="Actual cost" [value]="f.summary.actualCost | gggsMoney: f.currency" [hint]="'forecast ' + (f.summary.forecastCost | gggsMoney: f.currency)" />
          <gggs-kpi-card label="Budget used" [value]="f.summary.budgetUsedPercent !== undefined ? (f.summary.budgetUsedPercent | number: '1.0-0') + '%' : '—'" [hint]="'of ' + (f.summary.budget | gggsMoney: f.currency)" />
          <gggs-kpi-card label="Gross margin" [value]="f.summary.marginPercent !== undefined ? (f.summary.marginPercent | number: '1.0-1') + '%' : '—'" [hint]="(f.summary.grossMargin | gggsMoney: f.currency) + ' · forecast ' + (f.summary.forecastMarginPercent ?? 0 | number: '1.0-1') + '%'" />
        </div>
        @if (f.summary.unpricedHours) {
          <p class="alert alert--warning">{{ f.summary.unpricedHours }} timesheet line(s) have no cost rate - add a rate card line or a team rate so labour cost is complete.</p>
        }

        @if (f.summary.months.length) {
          <section class="panel stack">
            <h3 class="fin__heading">By month</h3>
            <gggs-chart kind="bar" title="Revenue and cost by month" [labels]="monthLabels()" [series]="monthSeries()" height="14rem" />
          </section>
        }

        <div class="fin__two">
          <section class="panel stack">
            <div class="toolbar">
              <h3 class="fin__heading">Budget against actual</h3>
              <span class="fin__spacer"></span>
              @if (f.canManage) {
                <button type="button" class="btn btn--sm" (click)="openBudget(f)">Budget</button>
                <button type="button" class="btn btn--link btn--sm" (click)="openTerms(f)">Commercial terms</button>
              }
            </div>
            <table class="table fin__table">
              <thead><tr><th scope="col">Category</th><th scope="col" class="fin__num">Budget</th><th scope="col" class="fin__num">Actual</th><th scope="col" class="fin__num">Committed</th></tr></thead>
              <tbody>
                @for (row of f.summary.categories; track row.category) {
                  <tr>
                    <td>{{ row.category }}</td>
                    <td class="fin__num">{{ row.budget | gggsMoney: f.currency }}</td>
                    <td class="fin__num" [class.fin__danger]="row.budget > 0 && row.actual > row.budget">{{ row.actual | gggsMoney: f.currency }}</td>
                    <td class="fin__num">{{ row.planned | gggsMoney: f.currency }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="muted">Cost variance {{ f.summary.costVariance | gggsMoney: f.currency }} (budget less forecast) · revenue variance {{ f.summary.revenueVariance | gggsMoney: f.currency }}.</p>
          </section>
          <section class="panel stack">
            <h3 class="fin__heading">Labour</h3>
            <table class="table fin__table">
              <thead><tr><th scope="col">Person</th><th scope="col" class="fin__num">Hours</th><th scope="col" class="fin__num">Cost</th><th scope="col" class="fin__num">Revenue</th></tr></thead>
              <tbody>
                @for (person of f.people; track person.employeeId) {
                  <tr>
                    <td>{{ person.employeeName ?? '?' }} <span class="muted">{{ person.costRate !== undefined ? '@ ' + (person.costRate | gggsMoney: f.currency) : 'no rate' }}</span></td>
                    <td class="fin__num">{{ person.hours | number: '1.0-1' }}{{ person.billableHours !== person.hours ? ' (' + (person.billableHours | number: '1.0-1') + ' billable)' : '' }}</td>
                    <td class="fin__num">{{ person.cost | gggsMoney: f.currency }}</td>
                    <td class="fin__num">{{ person.revenue | gggsMoney: f.currency }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="muted">No approved hours yet.</td></tr>
                }
              </tbody>
            </table>
          </section>
        </div>

        <section class="panel stack">
          <div class="toolbar">
            <h3 class="fin__heading">Invoices</h3>
            <span class="fin__spacer"></span>
            @if (f.canManage) {
              <button type="button" class="btn btn--sm" (click)="openInvoice()"><i class="pi pi-plus" aria-hidden="true"></i> Invoice</button>
            }
          </div>
          @for (invoice of f.invoices; track invoice.id) {
            <div class="fin__row">
              <span><strong>{{ invoice.number }}</strong>{{ invoice.milestoneName ? ' · ' + invoice.milestoneName : '' }}</span>
              <span>{{ invoice.amount | gggsMoney: f.currency }} + tax {{ invoice.taxAmount | gggsMoney: f.currency }}</span>
              <span class="muted" [class.fin__danger]="invoice.isOverdue">{{ invoice.invoiceDate | date: 'd MMM y' }}{{ invoice.dueDate ? ' · due ' + (invoice.dueDate | date: 'd MMM') : '' }}{{ invoice.paidOn ? ' · paid ' + (invoice.paidOn | date: 'd MMM') : '' }}</span>
              <span class="toolbar">
                <gggs-status-badge [status]="invoice.isOverdue ? 'Overdue' : invoice.status" />
                @if (f.canManage && invoice.status === 'Draft') {
                  <button type="button" class="btn btn--link btn--sm" (click)="invoiceAction(invoice, 'send')">Send</button>
                }
                @if (f.canManage && invoice.status === 'Sent') {
                  <button type="button" class="btn btn--link btn--sm" (click)="openPaid(invoice)">Paid</button>
                }
                @if (f.canManage && (invoice.status === 'Draft' || invoice.status === 'Sent')) {
                  <button type="button" class="btn btn--link btn--sm" (click)="invoiceAction(invoice, 'cancel')">Cancel</button>
                }
              </span>
            </div>
          } @empty {
            <p class="muted">No invoices yet.</p>
          }
        </section>

        <div class="fin__two">
          <section class="panel stack">
            <div class="toolbar">
              <h3 class="fin__heading">Other costs</h3>
              <span class="fin__spacer"></span>
              @if (f.canManage) {
                <button type="button" class="btn btn--sm" (click)="openCost(null)"><i class="pi pi-plus" aria-hidden="true"></i> Cost</button>
              }
            </div>
            @for (cost of f.costs; track cost.id) {
              <div class="fin__row fin__row--small">
                <span><strong>{{ cost.description }}</strong><br /><span class="muted">{{ cost.category }}{{ cost.vendor ? ' · ' + cost.vendor : '' }}{{ cost.planned ? ' · committed' : '' }}</span></span>
                <span>{{ cost.amount | gggsMoney: f.currency }}</span>
                <span class="muted">{{ cost.date | date: 'd MMM y' }}</span>
                <span>
                  @if (f.canManage) {
                    <button type="button" class="btn btn--link btn--sm" (click)="openCost(cost)">Edit</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="deleteCost(cost)">Remove</button>
                  }
                </span>
              </div>
            } @empty {
              <p class="muted">Vendor, expense and infrastructure costs go here. Labour comes from timesheets.</p>
            }
          </section>
          <section class="panel stack">
            <div class="toolbar">
              <h3 class="fin__heading">Rate card</h3>
              <span class="fin__spacer"></span>
              @if (f.canManage) {
                <button type="button" class="btn btn--sm" (click)="openRate()"><i class="pi pi-plus" aria-hidden="true"></i> Rate</button>
              }
            </div>
            @for (rate of f.rates; track rate.id) {
              <div class="fin__row fin__row--small">
                <span><strong>{{ rate.employeeName ?? rate.roleName ?? roleName(rate.projectRoleId) }}</strong> <span class="muted">{{ rate.employeeId ? 'person' : 'role' }}</span></span>
                <span>bill {{ rate.billingRate !== undefined ? (rate.billingRate | gggsMoney: f.currency) : '—' }} / cost {{ rate.costRate !== undefined ? (rate.costRate | gggsMoney: f.currency) : '—' }}</span>
                <span class="muted">from {{ rate.effectiveFrom | date: 'd MMM y' }}</span>
                <span>
                  @if (f.canManage) {
                    <button type="button" class="btn btn--link btn--sm" (click)="deleteRate(rate)">Remove</button>
                  }
                </span>
              </div>
            } @empty {
              <p class="muted">Without a rate card, each team member's own billing and cost rate is used.</p>
            }
          </section>
        </div>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="open" [title]="dialogTitle()" [busy]="busy()" [error]="formError()" (save)="save()" size="lg">
      @switch (dialog()) {
        @case ('budget') {
          @for (category of categories; track category) {
            <label class="field">
              <span class="field__label">{{ category }}</span>
              <input type="number" min="0" step="1000" [value]="budget()[category] ?? ''" (input)="setBudget(category, $any($event.target).value)" />
            </label>
          }
        }
        @case ('terms') {
          <label class="field">
            <span class="field__label">Contract value (overrides the contract's{{ finance()?.contractValueFromContract ? ', ' + (finance()?.contractValueFromContract | gggsMoney) : '' }})</span>
            <input type="number" min="0" [value]="contractValue() ?? ''" (input)="contractValue.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
          </label>
          @if (finance()?.billingType === 'Retainer') {
            <label class="field">
              <span class="field__label">Monthly retainer fee</span>
              <input type="number" min="0" [value]="retainer() ?? ''" (input)="retainer.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
            </label>
          }
          @if (finance()?.billingType === 'Milestone') {
            <p class="field__label">Billed when each milestone is achieved</p>
            @for (milestone of finance()?.milestones ?? []; track milestone.milestoneId) {
              <label class="field">
                <span class="field__label">{{ milestone.name }} <span class="muted">({{ milestone.status }})</span></span>
                <input type="number" min="0" [value]="milestoneAmount(milestone.milestoneId)" (input)="setMilestone(milestone.milestoneId, $any($event.target).value)" />
              </label>
            }
          }
        }
        @case ('rate') {
          <label class="field">
            <span class="field__label">For a role</span>
            <select [value]="rateRole()" (change)="rateRole.set($any($event.target).value)">
              <option value="">- or a named person below -</option>
              @for (role of roles(); track role.id) {
                <option [value]="role.id">{{ role.name }}</option>
              }
            </select>
          </label>
          @if (!rateRole()) {
            <label class="field">
              <span class="field__label">Person</span>
              <gggs-employee-select ariaLabel="Person" [ngModel]="ratePerson()" (ngModelChange)="ratePerson.set($event)" />
            </label>
          }
          <div class="fin__grid">
            <label class="field">
              <span class="field__label">Billing rate (an hour)</span>
              <input type="number" min="0" [value]="billingRate() ?? ''" (input)="billingRate.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Cost rate (an hour)</span>
              <input type="number" min="0" [value]="costRate() ?? ''" (input)="costRate.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">From</span>
              <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
            </label>
          </div>
        }
        @case ('cost') {
          <div class="fin__grid">
            <label class="field">
              <span class="field__label">Category</span>
              <select [value]="costCategory()" (change)="costCategory.set($any($event.target).value)">
                @for (category of nonLabour; track category) {
                  <option [value]="category">{{ category }}</option>
                }
              </select>
            </label>
            <label class="field">
              <span class="field__label">Amount</span>
              <input type="number" min="0" [value]="amount() || ''" (input)="amount.set(+$any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Date</span>
              <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Vendor</span>
              <input type="text" maxlength="200" [value]="vendor()" (input)="vendor.set($any($event.target).value)" />
            </label>
          </div>
          <label class="field">
            <span class="field__label">Description</span>
            <input type="text" maxlength="300" [value]="description()" (input)="description.set($any($event.target).value)" />
          </label>
          <label class="fin__flag">
            <input type="checkbox" [checked]="planned()" (change)="planned.set($any($event.target).checked)" />
            <span>Committed, not yet incurred (counts in the forecast only)</span>
          </label>
        }
        @case ('invoice') {
          <div class="fin__grid">
            <label class="field">
              <span class="field__label">Number (blank to number it)</span>
              <input type="text" maxlength="40" [value]="invoiceNumber()" (input)="invoiceNumber.set($any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Invoice date</span>
              <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Due date</span>
              <input type="date" [value]="dueDate()" (change)="dueDate.set($any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Amount (before tax)</span>
              <input type="number" min="0" [value]="amount() || ''" (input)="amount.set(+$any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Tax</span>
              <input type="number" min="0" [value]="tax()" (input)="tax.set(+$any($event.target).value)" />
            </label>
            @if (finance()?.milestones?.length) {
              <label class="field">
                <span class="field__label">For milestone</span>
                <select [value]="milestoneId()" (change)="milestoneId.set($any($event.target).value)">
                  <option value="">None</option>
                  @for (milestone of finance()?.milestones ?? []; track milestone.milestoneId) {
                    <option [value]="milestone.milestoneId">{{ milestone.name }}</option>
                  }
                </select>
              </label>
            }
          </div>
          <label class="field">
            <span class="field__label">Notes</span>
            <input type="text" maxlength="1000" [value]="description()" (input)="description.set($any($event.target).value)" />
          </label>
        }
        @case ('paid') {
          <label class="field">
            <span class="field__label">Paid on</span>
            <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
          </label>
        }
      }
    </gggs-form-dialog>
  `,
  styles: `
    .fin__kpis {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(9rem, 45%), 1fr));
      gap: var(--gggs-space-3);
    }

    .fin__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .fin__spacer {
      flex: 1;
    }

    .fin__two {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--gggs-space-3);
      align-items: start;
    }



    .fin__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .fin__row {
      display: grid;
      grid-template-columns: 1.4fr 1.6fr 1.6fr auto;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .fin__row--small {
      font-size: var(--gggs-font-size-sm);
    }

    .fin__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .fin__flag {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .fin__danger {
      color: var(--gggs-color-danger);
    }

    .fin__warning {
      color: var(--gggs-color-warning);
    }

    @media (max-width: 56rem) {
      .fin__two,
      .fin__row {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class FinancialsTab {
  private readonly api = inject(ProjectFinancialsApi);
  private readonly projects = inject(ProjectsApi);

  readonly projectId = input.required<string>();

  protected readonly categories = COST_CATEGORIES;
  protected readonly nonLabour: readonly CostCategory[] = ['Vendor', 'Expense', 'Infrastructure', 'Other'];
  protected readonly humanize = humanize;
  protected readonly finance = signal<ProjectFinance | null>(null);
  protected readonly roles = signal<readonly ProjectRole[]>([]);
  protected readonly dialog = signal<Dialog>(null);
  protected readonly open = signal(false);
  protected readonly editingCost = signal<FinanceCost | null>(null);
  protected readonly target = signal<FinanceInvoice | null>(null);
  protected readonly budget = signal<Partial<Record<CostCategory, number>>>({});
  protected readonly contractValue = signal<number | null>(null);
  protected readonly retainer = signal<number | null>(null);
  protected readonly milestoneAmounts = signal<Record<string, number>>({});
  protected readonly rateRole = signal('');
  protected readonly ratePerson = signal<string | null>(null);
  protected readonly billingRate = signal<number | null>(null);
  protected readonly costRate = signal<number | null>(null);
  protected readonly costCategory = signal<CostCategory>('Vendor');
  protected readonly amount = signal(0);
  protected readonly tax = signal(0);
  protected readonly date = signal(today());
  protected readonly dueDate = signal('');
  protected readonly vendor = signal('');
  protected readonly description = signal('');
  protected readonly planned = signal(false);
  protected readonly invoiceNumber = signal('');
  protected readonly milestoneId = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly monthLabels = computed(() => this.finance()?.summary.months.map((month) => month.month.slice(0, 7)) ?? []);
  protected readonly monthSeries = computed(() => {
    const months = this.finance()?.summary.months ?? [];
    return [
      { label: 'Revenue', values: months.map((month) => month.revenue) },
      { label: 'Cost', values: months.map((month) => month.labourCost + month.otherCost) },
      { label: 'Billed', values: months.map((month) => month.billed) },
    ];
  });
  protected readonly dialogTitle = computed(() => {
    switch (this.dialog()) {
      case 'budget':
        return 'Budget by category';
      case 'terms':
        return 'Commercial terms';
      case 'rate':
        return 'Rate card line';
      case 'cost':
        return this.editingCost() ? 'Edit cost' : 'Record a cost';
      case 'invoice':
        return 'Raise an invoice';
      case 'paid':
        return 'Invoice paid';
      default:
        return '';
    }
  });

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected roleName(id: string | undefined): string {
    return this.roles().find((role) => role.id === id)?.name ?? 'Role';
  }

  protected openBudget(finance: ProjectFinance): void {
    this.budget.set(Object.fromEntries(finance.budget.map((line) => [line.category, line.amount])));
    this.show('budget');
  }

  protected setBudget(category: CostCategory, value: string): void {
    this.budget.update((budget) => ({ ...budget, [category]: value === '' ? undefined : Number(value) }));
  }

  protected openTerms(finance: ProjectFinance): void {
    this.contractValue.set(finance.contractValueOverride ?? null);
    this.retainer.set(finance.retainerMonthlyFee ?? null);
    this.milestoneAmounts.set(Object.fromEntries(finance.milestones.filter((milestone) => milestone.amount !== undefined).map((milestone) => [milestone.milestoneId, milestone.amount ?? 0])));
    this.show('terms');
  }

  protected milestoneAmount(id: string): number | string {
    return id in this.milestoneAmounts() ? this.milestoneAmounts()[id] : '';
  }

  protected setMilestone(id: string, value: string): void {
    this.milestoneAmounts.update((amounts) => {
      const next = { ...amounts };
      if (value === '') {
        delete next[id];
      } else {
        next[id] = Number(value);
      }

      return next;
    });
  }

  protected async openRate(): Promise<void> {
    if (!this.roles().length) {
      this.roles.set(await this.projects.roles());
    }

    this.rateRole.set('');
    this.ratePerson.set(null);
    this.billingRate.set(null);
    this.costRate.set(null);
    this.date.set(today());
    this.show('rate');
  }

  protected openCost(cost: FinanceCost | null): void {
    this.editingCost.set(cost);
    this.costCategory.set(cost?.category ?? 'Vendor');
    this.amount.set(cost?.amount ?? 0);
    this.date.set(cost?.date ?? today());
    this.vendor.set(cost?.vendor ?? '');
    this.description.set(cost?.description ?? '');
    this.planned.set(cost?.planned ?? false);
    this.show('cost');
  }

  protected openInvoice(): void {
    this.invoiceNumber.set('');
    this.date.set(today());
    this.dueDate.set('');
    this.amount.set(this.finance()?.summary.unbilled ?? 0);
    this.tax.set(0);
    this.milestoneId.set('');
    this.description.set('');
    this.show('invoice');
  }

  protected openPaid(invoice: FinanceInvoice): void {
    this.target.set(invoice);
    this.date.set(today());
    this.show('paid');
  }

  protected async invoiceAction(invoice: FinanceInvoice, action: 'send' | 'cancel'): Promise<void> {
    await this.act(() => this.api.invoiceAction(this.projectId(), invoice.id, action));
  }

  protected async deleteCost(cost: FinanceCost): Promise<void> {
    await this.act(() => this.api.deleteCost(this.projectId(), cost.id));
  }

  protected async deleteRate(rate: FinanceRate): Promise<void> {
    await this.act(() => this.api.deleteRate(this.projectId(), rate.id));
  }

  protected async save(): Promise<void> {
    const id = this.projectId();
    this.busy.set(true);
    this.formError.set(null);
    try {
      switch (this.dialog()) {
        case 'budget':
          this.finance.set(
            await this.api.saveBudget(
              id,
              Object.entries(this.budget())
                .filter(([, amount]) => amount !== undefined)
                .map(([category, amount]) => ({ category: category as CostCategory, amount: amount ?? 0 })),
            ),
          );
          break;
        case 'terms':
          this.finance.set(await this.api.saveProfile(id, { contractValue: this.contractValue(), retainerMonthlyFee: this.retainer(), milestoneAmounts: this.milestoneAmounts() }));
          break;
        case 'rate':
          await this.api.saveRate(id, null, {
            projectRoleId: this.rateRole() || null,
            employeeId: this.rateRole() ? null : this.ratePerson(),
            billingRate: this.billingRate(),
            costRate: this.costRate(),
            effectiveFrom: this.date(),
          });
          break;
        case 'cost':
          await this.api.saveCost(id, this.editingCost()?.id ?? null, {
            category: this.costCategory(),
            date: this.date(),
            amount: this.amount(),
            vendor: this.vendor().trim() || null,
            description: this.description().trim(),
            planned: this.planned(),
          });
          break;
        case 'invoice':
          await this.api.saveInvoice(id, null, this.invoiceNumber().trim() || null, {
            invoiceDate: this.date(),
            dueDate: this.dueDate() || null,
            amount: this.amount(),
            taxAmount: this.tax(),
            milestoneId: this.milestoneId() || null,
            notes: this.description().trim() || null,
          });
          break;
        case 'paid': {
          const invoice = this.target();
          if (invoice) {
            await this.api.invoicePaid(id, invoice.id, this.date());
          }

          break;
        }
        default:
          break;
      }

      this.open.set(false);
      await this.load(id);
    } catch (error) {
      this.formError.set(message(error, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private show(dialog: Dialog): void {
    this.dialog.set(dialog);
    this.formError.set(null);
    this.open.set(true);
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    try {
      this.finance.set(await this.api.project(projectId));
      this.error.set(null);
    } catch (error) {
      this.error.set(message(error, 'Financials could not be loaded.'));
    }
  }
}
