import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { EmploymentType } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';
import { AssignmentFields } from '../shared/assignment-fields';

/** Hiring a new employee: their core record and their first employment record, in one call. */
@Component({
  selector: 'gggs-hire-employee-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, AssignmentFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Hire employee" subtitle="Creates the employee and their first assignment.">
      <a routerLink="/employees" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <h2 class="panel__heading">Basic details</h2>
      <div class="grid">
        <label class="field">
          <span class="field__label">Employee code</span>
          <input type="text" formControlName="employeeCode" maxlength="30" required />
        </label>

        <label class="field">
          <span class="field__label">First name</span>
          <input type="text" formControlName="firstName" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Middle name (optional)</span>
          <input type="text" formControlName="middleName" maxlength="100" />
        </label>

        <label class="field">
          <span class="field__label">Last name (optional)</span>
          <input type="text" formControlName="lastName" maxlength="100" />
        </label>

        <label class="field">
          <span class="field__label">Work email (optional)</span>
          <input type="email" formControlName="workEmail" maxlength="256" />
        </label>

        <label class="field">
          <span class="field__label">Date of joining</span>
          <input type="date" formControlName="dateOfJoining" required />
        </label>
      </div>

      <h2 class="panel__heading">Assignment</h2>
      <gggs-assignment-fields [formGroup]="form.controls.assignment" />

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Hiring…' : 'Hire' }}
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

    .panel__heading {
      margin: 0.5rem 0 0;
      font-size: 1rem;
    }

    .grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
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
      background: var(--gggs-color-primary, #2f5fd6);
      border-color: transparent;
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class HireEmployeePage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    employeeCode: ['', [Validators.required, Validators.maxLength(30)]],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    middleName: [''],
    lastName: [''],
    workEmail: ['', Validators.email],
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
      const employee = await this.api.hire({
        employeeCode: value.employeeCode ?? '',
        firstName: value.firstName ?? '',
        middleName: value.middleName || undefined,
        lastName: value.lastName || undefined,
        workEmail: value.workEmail || undefined,
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

      await this.router.navigate(['/employees', employee.id]);
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errorCode === 'EMPLOYEE_CODE_IN_USE') {
      return 'That employee code is already in use.';
    }

    if (error.status === 404) {
      return 'One of the chosen organizational references could not be found.';
    }
  }

  return 'The employee could not be hired. Please try again.';
}
