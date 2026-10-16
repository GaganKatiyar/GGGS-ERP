import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { ComponentItem, DeclarationRow, DeclarationStatus, DeclarationView, InputItem } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi } from '@gggs/shared/money';
import { Drawer, EmptyState, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { financialYearLabel, financialYearOf, message, recentFinancialYears, thisMonth } from './payroll-shared';

/** One-off payroll inputs for a month (spec §21 "variable inputs"): bonus, incentive, overtime, arrears, recoveries. */
@Component({
  selector: 'gggs-payroll-inputs-page',
  imports: [FormsModule, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payroll inputs" subtitle="One-off amounts for a month. Bonuses approved in Compensation and overtime from timesheets arrive here too.">
      <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> Add input</button>
    </gggs-page-header>
    <section class="panel toolbar pay__filters">
      <label class="field">
        <span class="field__label">Month</span>
        <input type="month" [value]="period().slice(0, 7)" (change)="setPeriod($any($event.target).value)" />
      </label>
      <span class="pay__spacer"></span>
      <strong>{{ total() | gggsMoney }}</strong>
    </section>
    <section class="panel stack">
      @for (input of inputs(); track input.id) {
        <div class="pay__row">
          <span><strong>{{ input.employeeName ?? input.employeeCode }}</strong> <span class="muted">{{ input.employeeCode }}</span></span>
          <span>{{ input.componentName }}{{ input.quantity ? ' · ' + input.quantity + ' h' : '' }}</span>
          <span><strong>{{ input.amount | gggsMoney }}</strong></span>
          <span class="muted">{{ input.description }} · {{ input.source }}</span>
          <span class="toolbar">
            <gggs-status-badge [status]="input.status" />
            @if (input.status === 'Pending' || input.status === 'Included') {
              <button type="button" class="btn btn--link btn--sm" (click)="cancel(input)">Remove</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-plus-circle" title="No inputs for this month" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="Add an input" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !componentId() || amount() <= 0 || !description().trim()" (save)="save()">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">What it is</span>
        <select [value]="componentId()" (change)="componentId.set($any($event.target).value)">
          <option value="">Choose</option>
          @for (component of inputComponents(); track component.id) {
            <option [value]="component.id">{{ component.name }} ({{ humanize(component.kind) }})</option>
          }
        </select>
      </label>
      <div class="pay__grid">
        <label class="field">
          <span class="field__label">Amount</span>
          <input type="number" min="0" step="1" [value]="amount() || ''" (input)="amount.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Hours (overtime)</span>
          <input type="number" min="0" step="0.5" [value]="quantity() ?? ''" (input)="quantity.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Description</span>
        <input type="text" maxlength="200" [value]="description()" (input)="description.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './payroll.css',
})
export class PayrollInputsPage {
  private readonly api = inject(PayrollApi);

  protected readonly humanize = humanize;
  protected readonly period = signal(thisMonth());
  protected readonly inputs = signal<readonly InputItem[]>([]);
  protected readonly components = signal<readonly ComponentItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly componentId = signal('');
  protected readonly amount = signal(0);
  protected readonly quantity = signal<number | null>(null);
  protected readonly description = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly inputComponents = computed(() => this.components().filter((component) => component.calculationType === 'Input' && component.isActive));
  protected readonly total = computed(() => this.inputs().reduce((sum, input) => sum + (input.kind === 'Deduction' ? -input.amount : input.amount), 0));

  constructor() {
    void this.load();
    this.api.components().then(
      (components) => this.components.set(components),
      () => undefined,
    );
  }

  protected setPeriod(month: string): void {
    if (month) {
      this.period.set(`${month}-01`);
      void this.load();
    }
  }

  protected openNew(): void {
    this.employeeId.set(null);
    this.componentId.set('');
    this.amount.set(0);
    this.quantity.set(null);
    this.description.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.addInput({ employeeId, period: this.period(), componentId: this.componentId(), amount: this.amount(), quantity: this.quantity(), description: this.description().trim() });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The input could not be added.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(input: InputItem): Promise<void> {
    try {
      await this.api.cancelInput(input.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The input could not be removed.'));
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.inputs.set(await this.api.inputs(this.period()));
    } catch (error) {
      this.error.set(message(error, 'Inputs could not be loaded.'));
    }
  }
}

/** Tax declarations (Form 12BB): what employees have declared, and verification against their proofs. */
@Component({
  selector: 'gggs-tax-declarations-page',
  imports: [DatePipe, MoneyPipe, PageHeader, StatusBadge, EmptyState, Drawer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Tax declarations" subtitle="Investment and rent declarations, verified against proofs before the year's last payrolls." />
    <section class="panel toolbar pay__filters">
      <label class="field">
        <span class="field__label">Financial year</span>
        <select [value]="year()" (change)="year.set(+$any($event.target).value); load()">
          @for (option of years; track option) {
            <option [value]="option">{{ yearLabel(option) }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          <option value="Submitted">Waiting for verification</option>
          <option value="Verified">Verified</option>
          <option value="Draft">Draft</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (row of rows(); track row.id) {
        <button type="button" class="pay__row" (click)="open(row)">
          <span><strong>{{ row.employeeName ?? row.employeeCode }}</strong> <span class="muted">{{ row.employeeCode }}</span></span>
          <span>{{ row.regime }} regime</span>
          <span>{{ row.declared | gggsMoney }} declared</span>
          <span class="muted">{{ row.submittedDate ? 'submitted ' + (row.submittedDate | date: 'd MMM') : '' }}</span>
          <gggs-status-badge [status]="row.status" />
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-file-edit" title="No declarations here" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-drawer [(visible)]="drawerOpen" [title]="(declaration()?.employeeName ?? 'Declaration') + ' · ' + (declaration()?.financialYearLabel ?? '')" width="min(40rem, 100vw)">
      @if (declaration(); as d) {
        <div class="stack">
          <div class="toolbar">
            <gggs-status-badge [status]="d.status" />
            <span>{{ d.regime }} regime</span>
          </div>
          @if (d.items.length) {
            <table class="pay__table">
              <thead><tr><th scope="col">Section</th><th scope="col">Item</th><th scope="col" class="pay__num">Declared</th><th scope="col" class="pay__num">Accepted</th></tr></thead>
              <tbody>
                @for (item of d.items; track $index; let index = $index) {
                  <tr>
                    <td>{{ item.section }}</td>
                    <td>{{ item.description }}</td>
                    <td class="pay__num">{{ item.declared | gggsMoney }}</td>
                    <td class="pay__num">
                      @if (canVerify && d.status !== 'Verified') {
                        <input type="number" min="0" [attr.aria-label]="'Accepted for ' + item.section" [value]="acceptedFor(index, item.declared)" (input)="setAccepted(index, +$any($event.target).value)" />
                      } @else {
                        {{ item.verified ?? item.declared | gggsMoney }}
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="muted">No deductions declared.</p>
          }
          <p>
            @if (d.monthlyRent) {
              Rent {{ d.monthlyRent | gggsMoney }} a month{{ d.metroCity ? ' in a metro' : '' }}{{ d.landlordPan ? ' · landlord PAN ' + d.landlordPan : '' }}.
            }
            @if (d.previousEmployerIncome) {
              Previous employer: income {{ d.previousEmployerIncome | gggsMoney }}, TDS {{ d.previousEmployerTds ?? 0 | gggsMoney }}.
            }
            @if (d.otherIncome) {
              Other income {{ d.otherIncome | gggsMoney }}.
            }
          </p>
          @if (d.verifierNotes) {
            <p class="muted">Payroll's note: {{ d.verifierNotes }}</p>
          }
          @if (canVerify) {
            @if (d.status !== 'Verified') {
              <label class="field">
                <span class="field__label">Note to the employee</span>
                <input type="text" maxlength="1000" [value]="notes()" (input)="notes.set($any($event.target).value)" />
              </label>
              <div class="toolbar">
                <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="verify(d)">Verify</button>
              </div>
            } @else {
              <button type="button" class="btn" [disabled]="busy()" (click)="reopen(d)">Reopen for changes</button>
            }
          }
        </div>
      }
    </gggs-drawer>
  `,
  styleUrl: './payroll.css',
})
export class TaxDeclarationsPage {
  private readonly api = inject(PayrollApi);

  protected readonly canVerify = inject(PermissionStore).has('payroll.tax-declaration.verify');
  protected readonly years = recentFinancialYears();
  protected readonly yearLabel = financialYearLabel;
  protected readonly year = signal(financialYearOf(thisMonth()));
  protected readonly status = signal<DeclarationStatus | ''>('Submitted');
  protected readonly rows = signal<readonly DeclarationRow[]>([]);
  protected readonly declaration = signal<DeclarationView | null>(null);
  protected readonly accepted = signal<Record<number, number>>({});
  protected readonly notes = signal('');
  protected readonly drawerOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected acceptedFor(index: number, declared: number): number {
    return index in this.accepted() ? this.accepted()[index] : declared;
  }

  protected setAccepted(index: number, value: number): void {
    this.accepted.update((current) => ({ ...current, [index]: value }));
  }

  protected async open(row: DeclarationRow): Promise<void> {
    this.declaration.set(null);
    this.accepted.set({});
    this.notes.set('');
    this.drawerOpen.set(true);
    try {
      this.declaration.set(await this.api.declaration(row.id));
    } catch (error) {
      this.error.set(message(error, 'The declaration could not be loaded.'));
    }
  }

  protected async verify(declaration: DeclarationView): Promise<void> {
    const id = declaration.id;
    if (!id) {
      return;
    }

    const accepted = declaration.items.map((item, index) => this.accepted()[index] ?? item.declared);
    await this.act(async () => this.declaration.set(await this.api.verifyDeclaration(id, accepted, this.notes().trim() || null)));
  }

  protected async reopen(declaration: DeclarationView): Promise<void> {
    const id = declaration.id;
    if (id) {
      await this.act(async () => this.declaration.set(await this.api.reopenDeclaration(id)));
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.rows.set(await this.api.declarations(this.year(), this.status()));
    } catch (error) {
      this.error.set(message(error, 'Declarations could not be loaded.'));
    }
  }

  private async act(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
