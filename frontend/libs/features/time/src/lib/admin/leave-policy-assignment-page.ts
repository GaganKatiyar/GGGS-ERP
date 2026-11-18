import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { EmployeeLeavePlan, LeaveCostCenterAssignment, LeavePlanAssignment, LeavePlanSummary } from '@gggs/shared/models';
import { LeaveApi, todayIso } from '@gggs/shared/time';
import { FormDialog, PageHeader, Skeleton, Tabs, type TabItem } from '@gggs/shared/ui';
import { assignmentChanges, centerKey, costCenterChanges, savedCenterPlan } from './leave-policy-model';

/**
 * Which leave policy each cost center and each engagement type follows, and the employees who follow
 * another one for a while. A cost center's policy comes before the engagement type's; an engagement
 * type without a policy is flagged: its employees cannot apply for leave unless their cost center has one.
 */
@Component({
  selector: 'gggs-leave-policy-assignment-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PageHeader, Tabs, Skeleton, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave policy assignment" subtitle="Give a cost center its own leave policy, or let its people follow their engagement type's. Single employees can follow another for a while.">
      <div class="toolbar">
        <a class="btn" routerLink="../policies">Leave policies</a>
      </div>
    </gggs-page-header>
    <p class="muted assign__order"><i class="pi pi-sort-amount-down" aria-hidden="true"></i> Which policy applies: an employee's own exception, then their cost center's policy for their engagement type, then their cost center's policy for everyone, then the organisation default.</p>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (tab() === 'centers') {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Cost centers">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Cost center</th>
              <th scope="col">Company · country</th>
              <th scope="col">People</th>
              <th scope="col">Everyone</th>
              <th scope="col"><span class="sr-only">By engagement type</span></th>
            </tr>
          </thead>
          <tbody>
            @for (row of centers(); track row.costCenterId) {
              <tr>
                <td>
                  <strong>{{ row.name }}</strong>
                  <span class="assign__code">{{ row.code }}{{ row.isActive ? '' : ' · closed' }}</span>
                  <span class="assign__summary">{{ summary(row) }}</span>
                </td>
                <td>{{ row.companyName ?? '—' }}@if (row.countryCode) { · {{ country(row.countryCode) }}}</td>
                <td>{{ row.employeeCount }}</td>
                <td>
                  <select [attr.aria-label]="'Leave policy for everyone on ' + row.name" (change)="chooseCenter(row.costCenterId, $any($event.target).value)">
                    <option value="" [selected]="!chosenCenter(row)">Organisation default</option>
                    @for (plan of activePlans(); track plan.id) {
                      <option [value]="plan.id" [selected]="plan.id === chosenCenter(row)">{{ plan.name }} ({{ plan.leaveYear }})</option>
                    }
                  </select>
                </td>
                <td class="assign__actions">
                  <button type="button" class="btn btn--sm" [attr.aria-expanded]="expanded().has(row.costCenterId)" (click)="toggle(row.costCenterId)">
                    <i class="pi" [class.pi-chevron-down]="!expanded().has(row.costCenterId)" [class.pi-chevron-up]="expanded().has(row.costCenterId)" aria-hidden="true"></i>
                    By engagement type
                    @if (typeCount(row)) {
                      <span class="assign__badge">{{ typeCount(row) }} set</span>
                    }
                  </button>
                </td>
              </tr>
              @if (expanded().has(row.costCenterId)) {
                <tr class="assign__sub">
                  <td colspan="5">
                    <table class="table assign__types">
                      <caption class="sr-only">Leave policy by engagement type on {{ row.name }}</caption>
                      <thead>
                        <tr><th scope="col">Engagement type on {{ row.name }}</th><th scope="col">Leave policy</th></tr>
                      </thead>
                      <tbody>
                        @for (type of assignments(); track type.employmentTypeCode) {
                          <tr>
                            <td>{{ type.label }}</td>
                            <td>
                              <select [attr.aria-label]="'Leave policy for ' + type.label + ' on ' + row.name" (change)="chooseCenter(row.costCenterId, $any($event.target).value, type.employmentTypeCode)">
                                <option value="" [selected]="!chosenCenter(row, type.employmentTypeCode)">{{ fallback(row, type) }}</option>
                                @for (plan of activePlans(); track plan.id) {
                                  <option [value]="plan.id" [selected]="plan.id === chosenCenter(row, type.employmentTypeCode)">{{ plan.name }} ({{ plan.leaveYear }})</option>
                                }
                              </select>
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </td>
                </tr>
              }
            } @empty {
              <tr><td colspan="5" class="muted">No cost centers yet. Add them under Org structure.</td></tr>
            }
          </tbody>
        </table>
      </div>
      <p class="muted">"Organisation default" uses the policy on the Organisation default tab for each engagement type. A change applies from today: balances are kept, and future accrual follows the new policy.</p>

      @if (centerChanges().length) {
        <div class="save-bar elevated" role="region" aria-label="Unsaved changes">
          <span><strong>{{ centerChanges().length }}</strong> unsaved {{ centerChanges().length === 1 ? 'change' : 'changes' }}</span>
          <button type="button" class="btn" [disabled]="busy()" (click)="discardCenters()">Discard</button>
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveCenters()">{{ busy() ? 'Saving…' : 'Save' }}</button>
        </div>
      }
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
        <span class="muted assign__spacer">An exception overrides the cost center's and engagement type's policy for one employee between two dates.</span>
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
    .assign__order { display: flex; gap: var(--gggs-space-2); align-items: center; margin: 0; }
    .assign__summary { display: block; margin-block-start: 2px; font-size: var(--gggs-font-size-sm); color: var(--gggs-color-text-muted); }
    .assign__badge { margin-inline-start: var(--gggs-space-1); padding: 0 8px; border-radius: 999px; background: var(--gggs-color-primary-soft, #fde8ec); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .assign__sub > td { background: var(--gggs-color-surface-muted, #f8fafc); padding-block: var(--gggs-space-2); }
    .assign__types { max-width: 44rem; margin-inline-start: var(--gggs-space-6); background: transparent; }
    .assign__actions { white-space: nowrap; }
    .save-bar { position: sticky; inset-block-end: var(--gggs-space-4); display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); box-shadow: var(--gggs-shadow-4); }
    .save-bar span { flex: 1; }
  `,
})
export class LeavePolicyAssignmentPage {
  private readonly api = inject(LeaveApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'centers', label: 'Cost centers' },
    { key: 'types', label: 'Organisation default' },
    { key: 'exceptions', label: 'Exceptions' },
  ];
  protected readonly tab = signal('centers');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly plans = signal<readonly LeavePlanSummary[]>([]);
  protected readonly assignments = signal<readonly LeavePlanAssignment[]>([]);
  protected readonly exceptions = signal<readonly EmployeeLeavePlan[]>([]);
  protected readonly edits = signal<ReadonlyMap<string, string | null>>(new Map());

  protected readonly centers = signal<readonly LeaveCostCenterAssignment[]>([]);
  protected readonly centerEdits = signal<ReadonlyMap<string, string | null>>(new Map());
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly activePlans = computed(() => this.plans().filter((plan) => plan.isActive));
  protected readonly changes = computed(() => assignmentChanges(this.assignments(), this.edits()));
  protected readonly centerChanges = computed(() => costCenterChanges(this.centers(), this.centerEdits()));

  private readonly countries = new Intl.DisplayNames(['en'], { type: 'region' });

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

  protected country(code: string): string {
    try {
      return this.countries.of(code) ?? code;
    } catch {
      return code;
    }
  }

  /** The policy shown for a cost center (everyone, or one engagement type there), counting unsaved edits. */
  protected chosenCenter(row: LeaveCostCenterAssignment, employmentTypeCode?: string): string | null {
    const key = centerKey(row.costCenterId, employmentTypeCode);
    return this.centerEdits().has(key) ? (this.centerEdits().get(key) ?? null) : savedCenterPlan(row, employmentTypeCode);
  }

  protected chooseCenter(costCenterId: string, planId: string, employmentTypeCode?: string): void {
    this.centerEdits.update((edits) => new Map(edits).set(centerKey(costCenterId, employmentTypeCode), planId || null));
    this.message.set(null);
  }

  protected toggle(costCenterId: string): void {
    this.expanded.update((open) => {
      const next = new Set(open);
      if (!next.delete(costCenterId)) {
        next.add(costCenterId);
      }
      return next;
    });
  }

  /** How many engagement types have a policy of their own on this cost center. */
  protected typeCount(row: LeaveCostCenterAssignment): number {
    return this.assignments().filter((type) => this.chosenCenter(row, type.employmentTypeCode)).length;
  }

  /** What an engagement type left blank gets: the cost center's everyone policy, else the organisation default. */
  protected fallback(row: LeaveCostCenterAssignment, type: LeavePlanAssignment): string {
    const everyone = this.chosenCenter(row);
    if (everyone) {
      return `Same as everyone (${this.planName(everyone)})`;
    }
    const organisation = this.edits().has(type.employmentTypeCode) ? this.edits().get(type.employmentTypeCode) : type.planId;
    return `Organisation default (${organisation ? this.planName(organisation) : 'no policy'})`;
  }

  /** "Everyone: India policy · Part-time: India PT", or "Organisation default". */
  protected summary(row: LeaveCostCenterAssignment): string {
    const parts: string[] = [];
    const everyone = this.chosenCenter(row);
    if (everyone) {
      parts.push(`Everyone: ${this.planName(everyone)}`);
    }
    for (const type of this.assignments()) {
      const own = this.chosenCenter(row, type.employmentTypeCode);
      if (own) {
        parts.push(`${type.label}: ${this.planName(own)}`);
      }
    }
    return parts.length ? parts.join(' · ') : 'Organisation default';
  }

  private planName(id: string): string {
    return this.plans().find((plan) => plan.id === id)?.name ?? 'a removed policy';
  }

  protected discardCenters(): void {
    this.centerEdits.set(new Map());
  }

  protected async saveCenters(): Promise<void> {
    await this.act(async () => {
      this.centers.set(await this.api.saveCostCenterAssignments(this.centerChanges()));
      this.centerEdits.set(new Map());
      this.plans.set(await this.api.plans());
      this.message.set("Saved. People on these cost centers follow the new policy from today.");
    });
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
      const [plans, assignments, exceptions, centers] = await Promise.all([this.api.plans(), this.api.assignments(), this.api.exceptions(), this.api.costCenterAssignments()]);
      this.plans.set(plans);
      this.assignments.set(assignments);
      this.exceptions.set(exceptions);
      this.centers.set(centers);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Assignments could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
