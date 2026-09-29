import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { EmploymentType } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';
import { AssignmentFields } from '../shared/assignment-fields';

/**
 * Bringing a former employee back. Unlike hiring, there is no employee code to enter and no
 * "already in use" check to fail - this is the same person's record, picking a fresh assignment
 * for a fresh stint (`Employee.Rehire` preserves their original hire date separately, so tenure
 * across the gap is never lost).
 */
@Component({
  selector: 'gggs-rehire-employee-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, AssignmentFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Rehire employee" subtitle="Starts a new stint with a fresh assignment.">
      <a [routerLink]="['/employees', id()]" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <label class="field">
        <span class="field__label">New date of joining</span>
        <input type="date" formControlName="dateOfJoining" required />
      </label>

      <gggs-assignment-fields [formGroup]="form.controls.assignment" [excludeEmployeeId]="id()" />

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Saving…' : 'Rehire' }}
      </button>
    </form>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      max-width: 60rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
      max-width: 20rem;
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
export class RehireEmployeePage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    dateOfJoining: ['', Validators.required],
    assignment: this.formBuilder.group({
      legalEntityId: ['', Validators.required],
      branchId: ['', Validators.required],
      departmentId: ['', Validators.required],
      designationId: ['', Validators.required],
      gradeId: [''],
      costCenterId: [''],
      reportingManagerId: [''],
      employmentType: ['Permanent' as EmploymentType, Validators.required],
      probationEndDate: [''],
    }),
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.rehire(this.id(), {
        dateOfJoining: value.dateOfJoining ?? '',
        assignment: {
          legalEntityId: value.assignment.legalEntityId ?? '',
          branchId: value.assignment.branchId ?? '',
          departmentId: value.assignment.departmentId ?? '',
          designationId: value.assignment.designationId ?? '',
          gradeId: value.assignment.gradeId || undefined,
          costCenterId: value.assignment.costCenterId || undefined,
          reportingManagerId: value.assignment.reportingManagerId || undefined,
          employmentType: (value.assignment.employmentType ?? 'Permanent') as EmploymentType,
          probationEndDate: value.assignment.probationEndDate || undefined,
        },
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
  if (error instanceof ApiError) {
    if (error.errorCode === 'EMPLOYEE_NOT_EXITED') {
      return 'Only a former employee who has exited can be rehired.';
    }

    if (error.status === 404) {
      return 'One of the chosen organizational references could not be found.';
    }
  }

  return 'The employee could not be rehired. Please try again.';
}
