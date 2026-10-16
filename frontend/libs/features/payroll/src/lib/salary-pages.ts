import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { EmployeeSalaryView, SalaryPreview, SalaryReason, SalaryRow, StructureItem } from '@gggs/shared/models';
import { SALARY_REASONS, humanize } from '@gggs/shared/models';
import { EmployeePayroll, MoneyPipe, PayrollApi } from '@gggs/shared/money';
import { EmptyState, FormDialog, PageHeader, Skeleton } from '@gggs/shared/ui';
import { message } from './payroll-shared';

/** Who is paid what (spec §21, §23): everyone in reach with their CTC, and who still has no salary. */
@Component({
  selector: 'gggs-salaries-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Salaries" subtitle="The CTC each person is paid on, and the structure it is laid out by." />
    <section class="panel toolbar pay__filters">
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" (keyup.enter)="load()" (search)="load()" />
      </label>
      <label class="pay__flag">
        <input type="checkbox" [checked]="withoutSalary()" (change)="withoutSalary.set($any($event.target).checked); load()" />
        <span>Only people without a salary</span>
      </label>
    </section>
    <section class="panel stack">
      @for (row of rows(); track row.employeeId) {
        <a class="pay__row" [routerLink]="['/payroll/salaries', row.employeeId]">
          <span><strong>{{ row.employeeName }}</strong> <span class="muted">{{ row.employeeCode }}</span></span>
          <span class="muted">{{ row.designation ?? '' }}</span>
          <span>{{ row.annualCtc !== undefined ? (row.annualCtc | gggsMoney) : 'No salary' }}</span>
          <span class="muted">{{ row.structureName ?? '' }}</span>
          <span class="muted">{{ row.effectiveFrom ? 'since ' + (row.effectiveFrom | date: 'MMM y') : '' }}</span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-wallet" title="No one matches" />
      }
      @if (hasMore()) {
        <button type="button" class="btn" (click)="more()">Show more</button>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './payroll.css',
})
export class SalariesPage {
  private readonly api = inject(PayrollApi);

  protected readonly rows = signal<readonly SalaryRow[]>([]);
  protected readonly search = signal('');
  protected readonly withoutSalary = signal(false);
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(append = false): Promise<void> {
    if (!append) {
      this.page.set(1);
    }

    try {
      const result = await this.api.salaries({ search: this.search(), withoutSalary: this.withoutSalary() || undefined, page: this.page(), pageSize: 50 });
      this.rows.set(append ? [...this.rows(), ...result.items] : result.items);
      this.hasMore.set(result.hasNextPage ?? result.items.length === 50);
    } catch (error) {
      this.error.set(message(error, 'Salaries could not be loaded.'));
    }
  }

  protected more(): void {
    this.page.update((page) => page + 1);
    void this.load(true);
  }
}

/** One person's salary: the breakdown, history, gratuity, payslips - and a new salary from a date. */
@Component({
  selector: 'gggs-employee-salary-page',
  imports: [DecimalPipe, RouterLink, MoneyPipe, PageHeader, Skeleton, FormDialog, EmployeePayroll],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.employeeName ?? 'Salary'" [subtitle]="v.employeeCode ?? ''">
        <a class="btn" [routerLink]="['/employees', v.employeeId]">Employee 360</a>
        @if (v.canManage) {
          <button type="button" class="btn btn--primary" (click)="openAssign(v)">{{ v.current ? 'New salary' : 'Assign salary' }}</button>
        }
      </gggs-page-header>
      <section class="panel">
        @for (key of [version()]; track key) {
          <gggs-employee-payroll [employeeId]="v.employeeId" />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (notice()) {
      <p class="pay__notice" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="assignOpen" title="Salary" saveLabel="Save salary" [busy]="busy()" [error]="formError()" [saveDisabled]="!structureId() || ctc() <= 0 || !effectiveFrom()" (save)="assign()" width="min(52rem, 96vw)">
      <div class="pay__grid">
        <label class="field">
          <span class="field__label">Structure</span>
          <select [value]="structureId()" (change)="structureId.set($any($event.target).value); preview()">
            <option value="">Choose a structure</option>
            @for (structure of structures(); track structure.id) {
              @if (structure.isActive) {
                <option [value]="structure.id">{{ structure.name }}</option>
              }
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Annual CTC</span>
          <input type="number" min="0" step="1000" [value]="ctc() || ''" (input)="ctc.set(+$any($event.target).value)" (change)="preview()" />
        </label>
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="effectiveFrom()" (change)="effectiveFrom.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Reason</span>
          <select [value]="reason()" (change)="reason.set($any($event.target).value)">
            @for (option of reasons; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
      </div>
      <label class="pay__flag">
        <input type="checkbox" [checked]="pfEnabled()" (change)="pfEnabled.set($any($event.target).checked); preview()" />
        <span>Deduct provident fund</span>
      </label>
      <label class="field">
        <span class="field__label">Notes</span>
        <input type="text" maxlength="500" [value]="notes()" (input)="notes.set($any($event.target).value)" />
      </label>
      @if (previewed(); as p) {
        <table class="pay__table">
          <thead><tr><th scope="col">Component</th><th scope="col" class="pay__num">Monthly</th><th scope="col" class="pay__num">Annual</th></tr></thead>
          <tbody>
            @for (line of p.lines; track line.code) {
              <tr [class.muted]="line.kind !== 'Earning'"><td>{{ line.name }}</td><td class="pay__num">{{ line.monthly | gggsMoney }}</td><td class="pay__num">{{ line.annual | gggsMoney }}</td></tr>
            }
          </tbody>
          <tfoot>
            <tr><th scope="row">Estimated take-home</th><td class="pay__num"><strong>{{ p.monthlyTakeHome | gggsMoney }}</strong></td><td class="pay__num">{{ p.monthlyTakeHome * 12 | gggsMoney }}</td></tr>
          </tfoot>
        </table>
        @for (issue of p.issues; track issue) {
          <p class="pay__warning">{{ issue }}</p>
        }
        @if (p.tax; as tax) {
          <p class="muted">TDS estimate: {{ tax.annualTax | gggsMoney }} a year on taxable income of {{ tax.taxableIncome | gggsMoney }} ({{ tax.regime }} regime, {{ tax.monthsRemaining | number }} months left).</p>
        }
      }
    </gggs-form-dialog>
  `,
  styleUrl: './payroll.css',
})
export class EmployeeSalaryPage {
  private readonly api = inject(PayrollApi);

  readonly employeeId = input.required<string>();

  protected readonly reasons = SALARY_REASONS;
  protected readonly humanize = humanize;
  protected readonly view = signal<EmployeeSalaryView | null>(null);
  protected readonly version = signal(0);
  protected readonly structures = signal<readonly StructureItem[]>([]);
  protected readonly assignOpen = signal(false);
  protected readonly structureId = signal('');
  protected readonly ctc = signal(0);
  protected readonly effectiveFrom = signal('');
  protected readonly reason = signal<SalaryReason>('Revision');
  protected readonly pfEnabled = signal(true);
  protected readonly notes = signal('');
  protected readonly previewed = signal<SalaryPreview | null>(null);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.employeeId());
    });
  }

  protected async openAssign(view: EmployeeSalaryView): Promise<void> {
    if (!this.structures().length) {
      this.structures.set(await this.api.structures());
    }

    this.structureId.set(view.current?.structureId ?? this.structures().find((structure) => structure.code === 'STANDARD')?.id ?? '');
    this.ctc.set(view.current?.annualCtc ?? 0);
    this.effectiveFrom.set(new Date().toISOString().slice(0, 10));
    this.reason.set(view.current ? 'Revision' : 'Hire');
    this.pfEnabled.set(view.current?.pfEnabled ?? true);
    this.notes.set('');
    this.previewed.set(null);
    this.formError.set(null);
    this.assignOpen.set(true);
    void this.preview();
  }

  protected async preview(): Promise<void> {
    if (!this.structureId() || this.ctc() <= 0) {
      this.previewed.set(null);
      return;
    }

    try {
      this.previewed.set(await this.api.preview(this.structureId(), { annualCtc: this.ctc(), pfEnabled: this.pfEnabled() }));
      this.formError.set(null);
    } catch (error) {
      this.formError.set(message(error, 'The preview could not be worked out.'));
    }
  }

  protected async assign(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.view.set(await this.api.assignSalary(this.employeeId(), {
        structureId: this.structureId(),
        annualCtc: this.ctc(),
        effectiveFrom: this.effectiveFrom(),
        reason: this.reason(),
        pfEnabled: this.pfEnabled(),
        notes: this.notes().trim() || null,
      }));
      this.version.update((version) => version + 1);
      this.assignOpen.set(false);
      this.notice.set('Salary saved.');
    } catch (error) {
      this.formError.set(message(error, 'The salary could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(employeeId: string): Promise<void> {
    try {
      this.view.set(await this.api.salary(employeeId));
    } catch (error) {
      this.error.set(message(error, 'The salary could not be loaded.'));
    }
  }
}
