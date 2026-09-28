import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { PageHeader } from '@gggs/shared/ui';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import type { LegalEntityListItem, PagedResult } from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/**
 * Legal entities: the companies inside this tenant. Payroll, statutory registration and payslip
 * branding all key off one of these, never off the tenant itself (architecture.md §1).
 */
@Component({
  selector: 'gggs-legal-entities-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, DataTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Legal entities"
      subtitle="Companies registered inside this tenant."
    >
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.legal-entity.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New legal entity' }}
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

          <label class="field">
            <span class="field__label">Country code</span>
            <input
              type="text"
              formControlName="countryCode"
              maxlength="2"
              placeholder="IN"
              required
            />
          </label>

          <label class="field">
            <span class="field__label">Currency</span>
            <input
              type="text"
              formControlName="currency"
              maxlength="3"
              placeholder="INR"
              required
            />
          </label>

          <label class="field">
            <span class="field__label">Registration number</span>
            <input type="text" formControlName="registrationNumber" maxlength="100" />
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

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No legal entities yet"
      emptyDescription="Create the first one to start building out branches and departments."
      (queryChange)="load($event)"
    />
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
      background: var(--gggs-color-primary, #2f5fd6);
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class LegalEntitiesPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly result = signal<PagedResult<LegalEntityListItem> | null>(null);

  protected readonly columns: readonly DataTableColumn<LegalEntityListItem>[] = [
    { field: 'code', header: 'Code', width: '10rem' },
    { field: 'name', header: 'Name', sortable: true },
    { field: 'countryCode', header: 'Country', width: '8rem' },
    { field: 'currency', header: 'Currency', width: '8rem' },
  ];

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    countryCode: ['', [Validators.required, Validators.maxLength(2)]],
    currency: ['', [Validators.required, Validators.maxLength(3)]],
    registrationNumber: [''],
  });

  constructor() {
    void this.load({ page: 1, pageSize: 25 });
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.isLoading.set(true);

    try {
      this.result.set(
        await this.api.listLegalEntities({
          page: query.page,
          pageSize: query.pageSize,
        }),
      );
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

    const { code, name, countryCode, currency, registrationNumber } =
      this.form.getRawValue();

    try {
      await this.api.createLegalEntity({
        code,
        name,
        countryCode,
        currency,
        registrationNumber: registrationNumber || undefined,
      });

      this.form.reset();
      this.isFormOpen.set(false);
      await this.load({ page: 1, pageSize: this.result()?.pageSize ?? 25 });
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

  return 'The legal entity could not be created. Please try again.';
}
