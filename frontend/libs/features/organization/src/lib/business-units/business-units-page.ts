import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { BusinessUnitListItem } from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/**
 * Business units: a grouping above department, for tenants that organize by division rather than
 * (or in addition to) legal entity - `emp.EmploymentRecord.BusinessUnitId` is optional for exactly
 * this reason, since not every tenant uses the concept.
 */
@Component({
  selector: 'gggs-business-units-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Business units"
      subtitle="Optional groupings above department, for tenants that organize by division."
    >
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.structure.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New business unit' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="panel__grid">
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" formControlName="code" maxlength="32" required />
          </label>

          <label class="field">
            <span class="field__label">Name</span>
            <input type="text" formControlName="name" maxlength="200" required />
          </label>
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Create' }}
        </button>
      </form>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (businessUnits().length === 0) {
      <gggs-empty-state
        title="No business units yet"
        description="Nothing uses this yet - departments and employees work fine without one."
      />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Name</th>
          </tr>
        </thead>
        <tbody>
          @for (unit of businessUnits(); track unit.id) {
            <tr>
              <td>{{ unit.code }}</td>
              <td>{{ unit.name }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__grid {
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
      border: 1px solid transparent;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #1d4ed8);
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .list-table {
      width: 100%;
      border-collapse: collapse;
    }

    .list-table th,
    .list-table td {
      padding: 0.5rem 0.75rem;
      text-align: left;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
    }
  `,
})
export class BusinessUnitsPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly businessUnits = signal<readonly BusinessUnitListItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.businessUnits.set(await this.api.listBusinessUnits());
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      await this.api.createBusinessUnit(this.form.getRawValue());
      this.form.reset();
      this.isFormOpen.set(false);
      await this.load();
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'ORGANIZATION_CODE_IN_USE') {
    return 'That code is already in use.';
  }

  return 'The business unit could not be created. Please try again.';
}
