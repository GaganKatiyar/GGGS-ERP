import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DocumentsApi } from '@gggs/shared/documents';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { DocumentCategory, DocumentTypeItem } from '@gggs/shared/models';

const CATEGORIES: readonly DocumentCategory[] = ['Employee', 'Company', 'Payroll', 'Recruitment'];

/**
 * The catalog of document types a tenant can file uploads under
 * (`Gggs.Documents.Api.Endpoints.DocumentTypeEndpoints`).
 *
 * Every upload names a type by code, so this screen exists before a single document can be
 * uploaded anywhere in the app - the Employee detail page's own upload form reads this same list,
 * filtered to the ones marked employee-uploadable.
 */
@Component({
  selector: 'gggs-document-types-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Document types" subtitle="What may be uploaded, and under what rules.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'document.type.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'Add document type' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="panel__grid">
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" formControlName="code" maxlength="50" required />
          </label>

          <label class="field">
            <span class="field__label">Name</span>
            <input type="text" formControlName="name" maxlength="200" required />
          </label>

          <label class="field">
            <span class="field__label">Category</span>
            <select formControlName="category">
              @for (category of categories; track category) {
                <option [value]="category">{{ category }}</option>
              }
            </select>
          </label>

          <label class="field">
            <span class="field__label">Max size (MB)</span>
            <input type="number" formControlName="maxSizeMb" min="1" max="500" required />
          </label>
        </div>

        <label class="field">
          <span class="field__label">Allowed extensions (comma-separated, e.g. pdf, jpg, png)</span>
          <input type="text" formControlName="allowedExtensions" required />
        </label>

        <label class="field field--checkbox">
          <input type="checkbox" formControlName="isEmployeeUploadable" />
          <span>Employees may upload this type themselves</span>
        </label>

        <label class="field field--checkbox">
          <input type="checkbox" formControlName="isSensitive" />
          <span>Sensitive</span>
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Save' }}
        </button>
      </form>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (types().length === 0) {
      <gggs-empty-state
        title="No document types yet"
        description="Add one before anyone can upload a document."
      />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Name</th>
            <th scope="col">Category</th>
            <th scope="col">Extensions</th>
            <th scope="col">Max size</th>
            <th scope="col">Employee upload</th>
          </tr>
        </thead>
        <tbody>
          @for (type of types(); track type.id) {
            <tr>
              <td>{{ type.code }}</td>
              <td>{{ type.name }}</td>
              <td>{{ type.category }}</td>
              <td>{{ type.allowedExtensions.join(', ') }}</td>
              <td>{{ type.maxSizeMb }} MB</td>
              <td>{{ type.isEmployeeUploadable ? 'Yes' : 'No' }}</td>
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
      max-width: 36rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
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

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
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
export class DocumentTypesPage {
  private readonly api = inject(DocumentsApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly categories = CATEGORIES;

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly types = signal<readonly DocumentTypeItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(50)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    category: ['Employee' as DocumentCategory, Validators.required],
    allowedExtensions: ['', Validators.required],
    maxSizeMb: [10, [Validators.required, Validators.min(1), Validators.max(500)]],
    isEmployeeUploadable: [false],
    isSensitive: [false],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.types.set(await this.api.listTypes());
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

    const raw = this.form.getRawValue();
    const allowedExtensions = raw.allowedExtensions
      .split(',')
      .map((extension) => extension.trim())
      .filter((extension) => extension.length > 0);

    if (allowedExtensions.length === 0) {
      this.errorMessage.set('At least one file extension must be allowed.');
      this.isSaving.set(false);
      return;
    }

    try {
      await this.api.createType({ ...raw, allowedExtensions });
      this.form.reset({
        code: '',
        name: '',
        category: 'Employee',
        allowedExtensions: '',
        maxSizeMb: 10,
        isEmployeeUploadable: false,
        isSensitive: false,
      });
      this.isFormOpen.set(false);
      await this.load();
    } catch {
      this.errorMessage.set('The document type could not be saved. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }
}
