import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import { EmployeesApi } from '../data/employees-api';

/**
 * Recording an exit. The last employment record is not closed by this - it stays open, describing
 * the assignment right up to departure, which is what a full-and-final settlement later reads.
 */
@Component({
  selector: 'gggs-exit-employee-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Record exit">
      <a [routerLink]="['/employees', id()]" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <label class="field">
        <span class="field__label">Exit date</span>
        <input type="date" formControlName="exitDate" required />
      </label>

      <label class="field field--checkbox">
        <input type="checkbox" formControlName="wasAbsconded" />
        <span>This employee absconded rather than exiting normally</span>
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--danger" [disabled]="isSaving()">
        {{ isSaving() ? 'Saving…' : 'Record exit' }}
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

    .field--checkbox {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
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

    .btn--danger {
      background: var(--gggs-color-danger, #b3261e);
      border-color: transparent;
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class ExitEmployeePage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    exitDate: ['', Validators.required],
    wasAbsconded: [false],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const { exitDate, wasAbsconded } = this.form.getRawValue();

    try {
      await this.api.exit(this.id(), {
        exitDate: exitDate ?? '',
        wasAbsconded: wasAbsconded ?? false,
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
    if (error.errorCode === 'EMPLOYEE_ALREADY_LEFT') {
      return 'This employee has already left.';
    }

    if (error.errorCode === 'EMPLOYEE_EXIT_BEFORE_JOINING') {
      return 'The exit date cannot be before the employee joined.';
    }
  }

  return 'The exit could not be recorded. Please try again.';
}
