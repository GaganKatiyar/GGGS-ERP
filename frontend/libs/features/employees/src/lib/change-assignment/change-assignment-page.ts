import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { EmploymentType } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';
import { AssignmentFields } from '../shared/assignment-fields';

/**
 * A new organizational assignment, effective a chosen date - what "transfer" and "promote" both
 * are, on the server (`EmployeeService.ChangeAssignmentAsync`) and here.
 *
 * One component rather than two near-identical ones: the only real difference between them is
 * which endpoint is called and what the page is titled, both of which come from the route's own
 * `data.mode` (bound as an input by the same `withComponentInputBinding()` that binds the `id`
 * path parameter - see `app.config.ts`).
 */
@Component({
  selector: 'gggs-change-assignment-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, AssignmentFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header [title]="title()" subtitle="Opens a new employment record effective the chosen date.">
      <a [routerLink]="['/employees', id()]" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <label class="field">
        <span class="field__label">Effective from</span>
        <input type="date" formControlName="effectiveFrom" required />
      </label>

      <gggs-assignment-fields [formGroup]="form.controls.assignment" [excludeEmployeeId]="id()" />

      <label class="field">
        <span class="field__label">Reason (optional)</span>
        <input type="text" formControlName="reason" maxlength="500" />
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Saving…' : submitLabel() }}
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
export class ChangeAssignmentPage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  readonly mode = input.required<'transfer' | 'promote'>();

  protected readonly title = computed(() =>
    this.mode() === 'promote' ? 'Promote employee' : 'Transfer employee',
  );
  protected readonly submitLabel = computed(() =>
    this.mode() === 'promote' ? 'Promote' : 'Transfer',
  );

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    effectiveFrom: ['', Validators.required],
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
    reason: [''],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      effectiveFrom: value.effectiveFrom ?? '',
      reason: value.reason || undefined,
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
    };

    try {
      if (this.mode() === 'promote') {
        await this.api.promote(this.id(), request);
      } else {
        await this.api.transfer(this.id(), request);
      }

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
    if (error.errorCode === 'EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF') {
      return 'An employee cannot be their own reporting manager.';
    }

    if (error.errorCode === 'EMPLOYMENT_RECORD_DATES_INVALID') {
      return 'The effective date must be on or after the current assignment started.';
    }

    if (error.status === 404) {
      return 'One of the chosen organizational references could not be found.';
    }
  }

  return 'The change could not be completed. Please try again.';
}
