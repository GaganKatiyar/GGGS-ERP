import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DocumentsApi } from '@gggs/shared/documents';
import type { DocumentCategory, DocumentTypeItem } from '@gggs/shared/models';
import { DOCUMENT_CATEGORY_LABELS } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** The kinds of document the organization keeps, and the rules each kind follows. */
@Component({
  selector: 'gggs-document-types-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, FormDialog, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Document types" subtitle="What kinds of document are kept, and whether employees see their own.">
      <div class="toolbar">
        <button type="button" class="btn btn--primary" (click)="openCreate()"><i class="pi pi-plus" aria-hidden="true"></i> Add type</button>
        <a class="btn" routerLink="..">Back to documents</a>
      </div>
    </gggs-page-header>

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (types().length === 0) {
      <gggs-empty-state title="No document types" />
    } @else {
      <section class="panel">
        <table class="table types__table">
          <thead>
            <tr>
              <th scope="col">Code</th>
              <th scope="col">Name</th>
              <th scope="col">Category</th>
              <th scope="col">Employee sees own</th>
              <th scope="col">Needs expiry</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="types__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (type of types(); track type.id) {
              <tr>
                <td><code>{{ type.code }}</code></td>
                <td>{{ type.name }} @if (type.isSystem) { <span class="muted">built-in</span> }</td>
                <td>{{ categories[type.category] }}</td>
                <td>{{ type.employeeVisible ? 'Yes' : 'No' }}</td>
                <td>{{ type.requiresExpiry ? 'Yes' : 'No' }}</td>
                <td><gggs-status-badge [status]="type.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="types__actions">
                  <button type="button" class="btn btn--link btn--sm" (click)="openEdit(type)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="toggle(type)">{{ type.isActive ? 'Retire' : 'Restore' }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit document type' : 'Add document type'" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" formControlName="code" maxlength="32" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" formControlName="name" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Category *</span>
          <select formControlName="category">
            @for (category of categoryKeys; track category) {
              <option [value]="category">{{ categories[category] }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="employeeVisible" /> Employees see their own</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="requiresExpiry" /> Needs an expiry date</span>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `


    .types__actions {
      white-space: nowrap;
    }

    .types__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class DocumentTypesPage {
  private readonly api = inject(DocumentsApi);

  protected readonly categories = DOCUMENT_CATEGORY_LABELS;
  protected readonly categoryKeys = Object.keys(DOCUMENT_CATEGORY_LABELS) as DocumentCategory[];
  protected readonly types = signal<readonly DocumentTypeItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<DocumentTypeItem | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: Validators.required }),
    name: new FormControl('', { nonNullable: true, validators: Validators.required }),
    category: new FormControl<DocumentCategory>('Other', { nonNullable: true }),
    employeeVisible: new FormControl(true, { nonNullable: true }),
    requiresExpiry: new FormControl(false, { nonNullable: true }),
  });

  constructor() {
    void this.load();
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', category: 'Other', employeeVisible: true, requiresExpiry: false });
    this.form.controls.code.enable();
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected openEdit(type: DocumentTypeItem): void {
    this.editing.set(type);
    this.form.reset({ code: type.code, name: type.name, category: type.category, employeeVisible: type.employeeVisible, requiresExpiry: type.requiresExpiry });
    this.form.controls.code.disable();
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Fill in the code and name.');
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();
    this.busy.set(true);
    this.error.set(null);
    try {
      const body = { code: value.code.trim(), name: value.name.trim(), category: value.category, employeeVisible: value.employeeVisible, requiresExpiry: value.requiresExpiry };
      if (editing) {
        await this.api.updateType(editing.id, body);
      } else {
        await this.api.createType(body);
      }
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The type could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(type: DocumentTypeItem): Promise<void> {
    this.listError.set(null);
    try {
      await this.api.setTypeActive(type.id, !type.isActive);
      await this.load();
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The type could not be changed.');
    }
  }

  private async load(): Promise<void> {
    try {
      this.types.set(await this.api.types(true));
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Document types could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
