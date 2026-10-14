import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { AssignmentChangeType, EmployeeProfile } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';
import { AssignmentFields, assignmentForm, toAssignmentInput, type AssignmentForm } from '../assignment-fields';

type LifecycleAction = 'confirm' | 'notice' | 'exit';

const CHANGE_TYPES: readonly { readonly value: AssignmentChangeType; readonly label: string }[] = [
  { value: 'Transfer', label: 'Transfer' },
  { value: 'Promotion', label: 'Promotion' },
  { value: 'ManagerChange', label: 'Manager change' },
  { value: 'Redesignation', label: 'Redesignation' },
  { value: 'EmploymentTermsChange', label: 'Change of employment terms' },
];

const today = () => new Date().toISOString().slice(0, 10);

/**
 * Where someone sits and the actions that move them: a transfer, promotion or manager change opens a
 * new employment record from its effective date; confirmation, notice and exit move the lifecycle.
 */
@Component({
  selector: 'gggs-employment-tab',
  imports: [DatePipe, ReactiveFormsModule, HasPermissionDirective, FormDialog, AssignmentFields, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = profile();
    <section class="panel stack">
      <div class="toolbar">
        @if (p.status !== 'Exited') {
          <button type="button" class="btn btn--primary" *gggsHasPermission="'employee.employment.manage'" (click)="openChange()">Change assignment</button>
          @if (!p.confirmationDate) {
            <button type="button" class="btn" *gggsHasPermission="'employee.employment.manage'" (click)="openLifecycle('confirm')">Confirm</button>
          }
          @if (p.status === 'Active') {
            <button type="button" class="btn" *gggsHasPermission="'employee.exit.manage'" (click)="openLifecycle('notice')">Start notice</button>
          }
          <button type="button" class="btn btn--danger" *gggsHasPermission="'employee.exit.manage'" (click)="openLifecycle('exit')">Record exit</button>
        }
      </div>

      <dl class="definition-list">
        <dt>Current since</dt><dd>{{ p.assignment.effectiveFrom | date: 'mediumDate' }}</dd>
        <dt>Designation</dt><dd>{{ p.assignment.designation ?? '—' }}</dd>
        <dt>Department</dt><dd>{{ p.assignment.department?.name ?? '—' }}</dd>
        <dt>Position</dt><dd>{{ p.assignment.position?.name ?? '—' }}</dd>
        <dt>Job</dt><dd>{{ p.assignment.job?.name ?? '—' }}</dd>
        <dt>Grade</dt><dd>{{ p.assignment.grade?.name ?? '—' }}</dd>
        <dt>Location</dt><dd>{{ p.assignment.location?.name ?? '—' }}</dd>
        <dt>Manager</dt><dd>{{ p.assignment.manager?.name ?? '—' }}</dd>
        <dt>Employment type</dt><dd>{{ p.assignment.employmentType.label }}</dd>
        <dt>Work mode</dt><dd>{{ p.assignment.workMode.label }}</dd>
      </dl>
    </section>

    <gggs-form-dialog [(visible)]="changeOpen" title="Change assignment" saveLabel="Apply change" width="min(52rem, 96vw)" [busy]="busy()" [error]="error()" (save)="saveChange()">
      <div class="stack" [formGroup]="change">
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Kind of change *</span>
            <select formControlName="changeType">
              @for (type of changeTypes; track type.value) {
                <option [value]="type.value">{{ type.label }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Effective from *</span>
            <input type="date" formControlName="effectiveFrom" />
            <span class="field__hint">Not before {{ p.assignment.effectiveFrom | date: 'mediumDate' }}.</span>
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Reason</span>
            <input type="text" formControlName="reason" maxlength="500" />
          </label>
        </div>
        @if (assignment(); as form) {
          <gggs-assignment-fields [form]="form" [employeeId]="p.id" />
        }
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="lifecycleOpen" [title]="lifecycleTitle()" [saveLabel]="lifecycleTitle()" [busy]="busy()" [error]="error()" (save)="saveLifecycle()">
      <div class="form-grid" [formGroup]="lifecycleForm">
        <label class="field">
          <span class="field__label">{{ lifecycleDateLabel() }} *</span>
          <input type="date" formControlName="date" />
        </label>
        @if (lifecycle() === 'exit') {
          <label class="field">
            <span class="field__label">Reason *</span>
            <gggs-lookup-select [list]="lists.exitReason" formControlName="reasonCode" ariaLabel="Exit reason" />
          </label>
          <p class="muted form-grid__full">Full and final settlement and the exit checklist arrive with Separation (M10). This records the date and reason.</p>
        }
      </div>
    </gggs-form-dialog>
  `,
})
export class EmploymentTab {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly profile = input.required<EmployeeProfile>();
  readonly changed = output<void>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly changeTypes = CHANGE_TYPES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly changeOpen = signal(false);
  protected readonly assignment = signal<AssignmentForm | null>(null);
  protected readonly change = new FormGroup({
    changeType: new FormControl<AssignmentChangeType>('Transfer', { nonNullable: true }),
    effectiveFrom: new FormControl(today(), { nonNullable: true, validators: Validators.required }),
    reason: new FormControl<string | null>(null),
  });

  protected readonly lifecycleOpen = signal(false);
  protected readonly lifecycle = signal<LifecycleAction>('confirm');
  protected readonly lifecycleForm = new FormGroup({
    date: new FormControl(today(), { nonNullable: true, validators: Validators.required }),
    reasonCode: new FormControl<string | null>(null),
  });

  protected readonly lifecycleTitle = computed(() => ({ confirm: 'Confirm employment', notice: 'Start notice period', exit: 'Record exit' })[this.lifecycle()]);
  protected readonly lifecycleDateLabel = computed(() => ({ confirm: 'Confirmation date', notice: 'Notice starts', exit: 'Last working day' })[this.lifecycle()]);

  protected openChange(): void {
    this.change.reset({ changeType: 'Transfer', effectiveFrom: today(), reason: null });
    this.assignment.set(assignmentForm(this.profile().assignment));
    this.error.set(null);
    this.changeOpen.set(true);
  }

  protected openLifecycle(action: LifecycleAction): void {
    this.lifecycle.set(action);
    this.lifecycleForm.reset({ date: today(), reasonCode: null });
    this.error.set(null);
    this.lifecycleOpen.set(true);
  }

  protected async saveChange(): Promise<void> {
    const form = this.assignment();
    if (!form || this.change.invalid) {
      this.error.set('Choose the kind of change and its effective date.');
      return;
    }

    const value = this.change.getRawValue();
    await this.run(
      () =>
        this.api.changeAssignment(this.profile().id, {
          changeType: value.changeType,
          effectiveFrom: value.effectiveFrom,
          reason: value.reason?.trim() || null,
          assignment: toAssignmentInput(form),
        }),
      () => this.changeOpen.set(false),
    );
  }

  protected async saveLifecycle(): Promise<void> {
    const { date, reasonCode } = this.lifecycleForm.getRawValue();
    const action = this.lifecycle();
    if (!date || (action === 'exit' && !reasonCode)) {
      this.error.set(action === 'exit' ? 'Choose the last working day and a reason.' : 'Choose a date.');
      return;
    }

    const body = action === 'confirm' ? { confirmationDate: date } : action === 'notice' ? { noticeStartDate: date } : { exitDate: date, exitReasonCode: reasonCode };
    await this.run(
      () => this.api.lifecycle(this.profile().id, action, body),
      () => this.lifecycleOpen.set(false),
    );
  }

  private async run(call: () => Promise<void>, close: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
      close();
      this.changed.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The change could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
