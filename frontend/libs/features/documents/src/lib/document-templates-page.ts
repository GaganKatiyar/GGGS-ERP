import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DocumentsApi } from '@gggs/shared/documents';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { DocumentTemplateItem, DocumentTypeItem } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** Letters written once and filled in per person - employment certificates, offer and relieving letters. */
@Component({
  selector: 'gggs-document-templates-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, FormDialog, EmployeeSelect, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Letter templates" subtitle="Write a letter once; generate it for anyone, ready to download or sign.">
      <div class="toolbar">
        <button type="button" class="btn btn--primary" (click)="openCreate()"><i class="pi pi-plus" aria-hidden="true"></i> Add template</button>
        <a class="btn" routerLink="..">Back to documents</a>
      </div>
    </gggs-page-header>

    @if (loading()) {
      <gggs-skeleton [lines]="4" />
    } @else if (templates().length === 0) {
      <gggs-empty-state title="No templates" description="Add a letter to generate it for employees." />
    } @else {
      <div class="templates">
        @for (template of templates(); track template.id) {
          <section class="panel stack">
            <div class="toolbar">
              <h2 class="templates__name">{{ template.name }}</h2>
              <gggs-status-badge [status]="template.isActive ? 'Active' : 'Inactive'" />
            </div>
            <p class="muted">{{ template.description ?? template.typeName }}</p>
            <div class="toolbar">
              @if (template.isActive) {
                <button type="button" class="btn btn--primary btn--sm" (click)="openGenerate(template)">Generate</button>
              }
              <button type="button" class="btn btn--sm" (click)="openEdit(template)">Edit</button>
              <button type="button" class="btn btn--link btn--sm" (click)="toggle(template)">{{ template.isActive ? 'Retire' : 'Restore' }}</button>
            </div>
          </section>
        }
      </div>
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="editOpen" [title]="editing() ? 'Edit template' : 'Add template'" size="lg" [busy]="busy()" [error]="error()" (save)="save()">
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
          <span class="field__label">Document type *</span>
          <select formControlName="documentTypeId">
            <option value="">Choose a type</option>
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Description</span>
          <input type="text" formControlName="description" maxlength="500" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Letter (HTML) *</span>
          <textarea rows="14" formControlName="body" class="templates__body"></textarea>
          <span class="field__hint">
            Placeholders: {{ placeholders }}. Values are inserted safely as text.
          </span>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="generateOpen" [title]="'Generate ' + (target()?.name ?? '')" saveLabel="Generate" [busy]="busy()" [error]="error()" (save)="generate()">
      <div class="form-grid" [formGroup]="generateForm">
        <label class="field form-grid__full">
          <span class="field__label">For *</span>
          <gggs-employee-select formControlName="employeeId" ariaLabel="Employee" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" placeholder="Template name and the person's name" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Ask to sign <span class="muted">(optional)</span></span>
          <gggs-employee-select formControlName="signerEmployeeId" ariaLabel="Signer" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .templates {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(18rem, 100%), 1fr));
      gap: var(--gggs-space-3);
    }

    .templates__name {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .templates__body {
      font-family: var(--gggs-font-family-mono, monospace);
    }
  `,
})
export class DocumentTemplatesPage {
  private readonly api = inject(DocumentsApi);
  private readonly router = inject(Router);

  protected readonly placeholders = '{{employeeName}}, {{employeeCode}}, {{officialEmail}}, {{designation}}, {{department}}, {{joiningDate}}, {{today}}';
  protected readonly templates = signal<readonly DocumentTemplateItem[]>([]);
  protected readonly types = signal<readonly DocumentTypeItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly editOpen = signal(false);
  protected readonly generateOpen = signal(false);
  protected readonly editing = signal<DocumentTemplateItem | null>(null);
  protected readonly target = signal<DocumentTemplateItem | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: Validators.required }),
    name: new FormControl('', { nonNullable: true, validators: Validators.required }),
    documentTypeId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    description: new FormControl<string | null>(null),
    body: new FormControl('', { nonNullable: true, validators: Validators.required }),
  });

  protected readonly generateForm = new FormGroup({
    employeeId: new FormControl<string | null>(null, Validators.required),
    title: new FormControl<string | null>(null),
    signerEmployeeId: new FormControl<string | null>(null),
  });

  constructor() {
    void this.load();
    void this.api.types().then((types) => this.types.set(types));
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', documentTypeId: '', description: null, body: '<h1>Title</h1>\n<p>Dear {{employeeName}},</p>\n' });
    this.form.controls.code.enable();
    this.error.set(null);
    this.editOpen.set(true);
  }

  protected openEdit(template: DocumentTemplateItem): void {
    this.editing.set(template);
    this.form.reset({ code: template.code, name: template.name, documentTypeId: template.documentTypeId, description: template.description ?? null, body: template.body });
    this.form.controls.code.disable();
    this.error.set(null);
    this.editOpen.set(true);
  }

  protected openGenerate(template: DocumentTemplateItem): void {
    this.target.set(template);
    this.generateForm.reset();
    this.error.set(null);
    this.generateOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Fill in the code, name, type and letter.');
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();
    await this.run(async () => {
      const body = { code: value.code.trim(), name: value.name.trim(), description: value.description?.trim() || undefined, documentTypeId: value.documentTypeId, body: value.body };
      if (editing) {
        await this.api.updateTemplate(editing.id, body);
      } else {
        await this.api.createTemplate(body);
      }
      this.editOpen.set(false);
      await this.load();
    });
  }

  protected async generate(): Promise<void> {
    const target = this.target();
    const value = this.generateForm.getRawValue();
    const employeeId = value.employeeId;
    if (!target || !employeeId) {
      this.error.set('Choose who the letter is for.');
      return;
    }

    await this.run(async () => {
      await this.api.generate(target.id, employeeId, value.title?.trim() || null, value.signerEmployeeId);
      this.generateOpen.set(false);
      void this.router.navigate(['/employees', employeeId], { queryParams: { tab: 'documents' } });
    });
  }

  protected async toggle(template: DocumentTemplateItem): Promise<void> {
    this.listError.set(null);
    try {
      await this.api.setTemplateActive(template.id, !template.isActive);
      await this.load();
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The template could not be changed.');
    }
  }

  private async run(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await this.api.templates());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Templates could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
