import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';

/**
 * Changing who an employee reports to, without touching anything else about their assignment -
 * the one field `EmployeeService.ChangeReportingManagerAsync` lets through on its own, carrying
 * the rest of the current record forward unchanged.
 */
@Component({
  selector: 'gggs-change-manager-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Change reporting manager">
      <a [routerLink]="['/employees', id()]" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <label class="field">
        <span class="field__label">Effective from</span>
        <input type="date" formControlName="effectiveFrom" required />
      </label>

      <label class="field">
        <span class="field__label">New reporting manager</span>
        <select formControlName="newReportingManagerId">
          <option value="">None</option>
          @for (employee of employees(); track employee.id) {
            @if (employee.id !== id()) {
              <option [value]="employee.id">{{ employee.displayName }}</option>
            }
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Reason (optional)</span>
        <input type="text" formControlName="reason" maxlength="500" />
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Saving…' : 'Change manager' }}
      </button>
    </form>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      max-width: 24rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #1d4ed8);
      border-color: transparent;
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class ChangeManagerPage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  // Fetched in full for the dropdown - the same v1 simplification AssignmentFields makes for the
  // same picker on the hire and transfer forms (see its own note on why).
  protected readonly employees = signal<readonly EmployeeListItem[]>([]);

  protected readonly form = this.formBuilder.group({
    effectiveFrom: ['', Validators.required],
    newReportingManagerId: [''],
    reason: [''],
  });

  constructor() {
    void this.loadEmployees();
  }

  private async loadEmployees(): Promise<void> {
    const result = await this.api.list({ page: 1, pageSize: 200 });
    this.employees.set(result.items);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const { effectiveFrom, newReportingManagerId, reason } = this.form.getRawValue();

    try {
      await this.api.changeManager(this.id(), {
        effectiveFrom: effectiveFrom ?? '',
        newReportingManagerId: newReportingManagerId || undefined,
        reason: reason || undefined,
      });

      await this.router.navigate(['/employees', this.id()]);
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF') {
    return 'An employee cannot be their own reporting manager.';
  }

  return 'The reporting manager could not be changed. Please try again.';
}
