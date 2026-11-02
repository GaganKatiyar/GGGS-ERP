import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { EmployeeLeavePlan, LeavePlanAssignment, LeavePlanSummary } from '@gggs/shared/models';
import { LeaveApi, todayIso } from '@gggs/shared/time';
import { FormDialog, PageHeader, Skeleton, Tabs, type TabItem } from '@gggs/shared/ui';
import { assignmentChanges } from './leave-policy-model';

/**
 * Which leave policy each engagement type follows, and the employees who follow another one for a
 * while. An engagement type without a policy is flagged: its employees cannot apply for leave.
 */
@Component({
  selector: 'gggs-leave-policy-assignment-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PageHeader, Tabs, Skeleton, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave policy assignment" subtitle="Every engagement type follows one leave policy; single employees can follow another for a while.">
      <div class="toolbar">
        <a class="btn" routerLink="../policies">Leave policies</a>
      </div>
    </gggs-page-header>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (tab() === 'types') {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Engagement types">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Engagement type</th>
              <th scope="col">Leave policy</th>
            </tr>
          </thead>
          <tbody>
            @for (row of assignments(); track row.employmentTypeCode) {
              <tr>
                <td>
                  <strong>{{ row.label }}</strong>
                  <span class="assign__code">{{ row.employmentTypeCode }}</span>
                </td>
                <td>
                  <select [attr.aria-label]="'Leave policy for ' + row.label" (change)="choose(row.employmentTypeCode, $any($event.target).value)">
                    <option value="" [selected]="!chosen(row)">No policy</option>
                    @for (plan of activePlans(); track plan.id) {
                      <option [value]="plan.id" [selected]="plan.id === chosen(row)">{{ plan.name }} ({{ plan.leaveYear }})</option>
                    }
                  </select>
                  @if (!chosen(row)) {
                    <span class="assign__warn"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i> Cannot apply for leave</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <p class="muted">New engagement types are added in Master data. A change applies from today: balances are kept, and future accrual follows the new policy.</p>

      @if (changes().length) {
        <div class="save-bar elevated" role="region" aria-label="Unsaved changes">
          <span><strong>{{ changes().length }}</strong> unsaved {{ changes().length === 1 ? 'change' : 'changes' }}</span>
          <button type="button" class="btn" [disabled]="busy()" (click)="discard()">Discard</button>
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveAssignments()">{{ busy() ? 'Saving…' : 'Save' }}</button>
        </div>
      }
    } @else {
      <div class="toolbar">
        <span class="muted assign__spacer">An exception overrides the engagement type's policy for one employee between two dates.</span>
        <button type="button" class="btn btn--primary btn--sm" (click)="openException(null)"><i class="pi pi-plus" aria-hidden="true"></i> Add exception</button>
      </div>
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Exceptions">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Employee</th>
              <th scope="col">Policy</th>
              <th scope="col">From</th>
              <th scope="col">Until</th>
              <th scope="col">Reason</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (row of exceptions(); track row.id) {
              <tr>
                <td><strong>{{ row.employeeName ?? 'Unknown' }}</strong> <span class="assign__code">{{ row.employeeCode }}</span></td>
                <td>{{ row.planName }}</td>
                <td>{{ row.effectiveFrom | date: 'mediumDate' }}</td>
                <td>{{ row.effectiveTo ? (row.effectiveTo | date: 'mediumDate') : 'Open-ended' }}</td>
                <td>{{ row.reason ?? '—' }}</td>
                <td class="assign__actions">
                  <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Edit exception for ' + row.employeeName" (click)="openException(row)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Remove exception for ' + row.employeeName" (click)="removeException(row)">Remove</button>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="muted">No exceptions: everyone follows their engagement type's policy.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (message()) {
      <p class="alert alert--success" role="status">{{ message() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="exceptionOpen" [title]="editing() ? 'Edit exception' : 'Add exception'" [busy]="busy()" [error]="dialogError()" (save)="saveException()">
      <div class="form-grid">
        <div class="field form-grid__full">
          <span class="field__label" id="exception-employee">Employee *</span>
          <gggs-employee-select [formControl]="employee" ariaLabel="Employee" />
        </div>
        <label class="field form-grid__full">
          <span class="field__label">Leave policy *</span>
          <select [value]="planId()" (change)="planId.set($any($event.target).value)">
            <option value="" disabled [selected]="!planId()">Choose a policy</option>
            @for (plan of activePlans(); track plan.id) {
              <option [value]="plan.id" [selected]="plan.id === planId()">{{ plan.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">From *</span>
          <input type="date" [value]="from()" (input)="from.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Until <span class="muted">(empty: open-ended)</span></span>
          <input type="date" [value]="until()" (input)="until.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Reason</span>
          <input type="text" maxlength="300" [value]="reason()" (input)="reason.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .assign__code { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .assign__warn { display: inline-flex; align-items: center; gap: var(--gggs-space-1); margin-inline-start: var(--gggs-space-3); color: var(--gggs-color-warning-strong); font-size: var(--gggs-font-size-sm); }
    .assign__spacer { margin-inline-end: auto; }
    .assign__actions { white-space: nowrap; }
    .save-bar { position: sticky; inset-block-end: var(--gggs-space-4); display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); box-shadow: var(--gggs-shadow-4); }
    .save-bar span { flex: 1; }
  `,
})
export class LeavePolicyAssignmentPage {
  private readonly api = inject(LeaveApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'types', label: 'Engagement types' },
    { key: 'exceptions', label: 'Exceptions' },
  ];
  protected readonly tab = signal('types');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly plans = signal<readonly LeavePlanSummary[]>([]);
  protected readonly assignments = signal<readonly LeavePlanAssignment[]>([]);
  protected readonly exceptions = signal<readonly EmployeeLeavePlan[]>([]);
  protected readonly edits = signal<ReadonlyMap<string, string | null>>(new Map());

  protected readonly activePlans = computed(() => this.plans().filter((plan) => plan.isActive));
  protected readonly changes = computed(() => assignmentChanges(this.assignments(), this.edits()));

  protected readonly exceptionOpen = signal(false);
  protected readonly editing = signal<EmployeeLeavePlan | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly employee = new FormControl<string | null>(null);
  protected readonly planId = signal('');
  protected readonly from = signal(todayIso());
  protected readonly until = signal('');
  protected readonly reason = signal('');

  constructor() {
    void this.load();
  }

  protected chosen(row: LeavePlanAssignment): string | null {
    return this.edits().has(row.employmentTypeCode) ? (this.edits().get(row.employmentTypeCode) ?? null) : (row.planId ?? null);
  }

  protected choose(code: string, planId: string): void {
    this.edits.update((edits) => new Map(edits).set(code, planId || null));
    this.message.set(null);
  }

  protected discard(): void {
    this.edits.set(new Map());
  }

  protected async saveAssignments(): Promise<void> {
    await this.act(async () => {
      this.assignments.set(await this.api.saveAssignments(this.changes()));
      this.edits.set(new Map());
      this.plans.set(await this.api.plans());
      this.message.set("Saved. Employees follow their engagement type's new policy from today.");
    });
  }

  protected openException(row: EmployeeLeavePlan | null): void {
    this.editing.set(row);
    this.employee.setValue(row?.employeeId ?? null);
    if (row) {
      this.employee.disable();
    } else {
      this.employee.enable();
    }
    this.planId.set(row?.planId ?? '');
    this.from.set(row?.effectiveFrom ?? todayIso());
    this.until.set(row?.effectiveTo ?? '');
    this.reason.set(row?.reason ?? '');
    this.dialogError.set(null);
    this.exceptionOpen.set(true);
  }

  protected async saveException(): Promise<void> {
    const employeeId = this.employee.value;
    if (!employeeId || !this.planId() || !this.from()) {
      this.dialogError.set('Choose the employee, the policy and the date it starts.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveException(this.editing()?.id ?? null, {
        employeeId,
        planId: this.planId(),
        effectiveFrom: this.from(),
        effectiveTo: this.until() || null,
        reason: this.reason().trim() || null,
      });
      this.exceptionOpen.set(false);
      this.exceptions.set(await this.api.exceptions());
      this.message.set('Saved.');
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The exception could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeException(row: EmployeeLeavePlan): Promise<void> {
    if (!confirm(`Remove ${row.employeeName ?? 'this employee'}'s exception? They go back to their engagement type's policy.`)) {
      return;
    }
    await this.act(async () => {
      await this.api.removeException(row.id);
      this.exceptions.set(await this.api.exceptions());
    });
  }

  private async act(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await call();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [plans, assignments, exceptions] = await Promise.all([this.api.plans(), this.api.assignments(), this.api.exceptions()]);
      this.plans.set(plans);
      this.assignments.set(assignments);
      this.exceptions.set(exceptions);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Assignments could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
