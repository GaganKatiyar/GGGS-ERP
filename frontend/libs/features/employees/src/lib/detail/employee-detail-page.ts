import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DocumentsApi, documentDownloadUrl } from '@gggs/shared/documents';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { DocumentItem, DocumentTypeItem, Employee, EmploymentRecord } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';

/**
 * One employee: their core record and their full employment history.
 *
 * The id comes from the route via `withComponentInputBinding()` (app.config.ts already enables
 * it), so this needs no `ActivatedRoute` injection of its own.
 */
@Component({
  selector: 'gggs-employee-detail-page',
  imports: [RouterLink, ReactiveFormsModule, SlicePipe, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (employee(); as employee) {
      <gggs-page-header [title]="employee.displayName" [subtitle]="subtitle(employee)">
        @if (employee.status !== 'Exited' && employee.status !== 'Absconded') {
          <a [routerLink]="['/employees', employee.id, 'transfer']" class="btn" *gggsHasPermission="'employee.employment.manage'">
            Transfer
          </a>
          <a [routerLink]="['/employees', employee.id, 'promote']" class="btn" *gggsHasPermission="'employee.employment.manage'">
            Promote
          </a>
          <a [routerLink]="['/employees', employee.id, 'change-manager']" class="btn" *gggsHasPermission="'employee.employment.manage'">
            Change manager
          </a>
        }
        @if (employee.status === 'PreBoarding' || employee.status === 'Probation') {
          <a [routerLink]="['/employees', employee.id, 'confirm']" class="btn" *gggsHasPermission="'employee.employment.manage'">
            Confirm
          </a>
        }
        @if (employee.status !== 'OnNotice' && employee.status !== 'Exited' && employee.status !== 'Absconded') {
          <button
            type="button"
            class="btn"
            *gggsHasPermission="'employee.employment.manage'"
            [disabled]="isActing()"
            (click)="serveNotice()"
          >
            Serve notice
          </button>
        }
        @if (employee.status !== 'Exited' && employee.status !== 'Absconded') {
          <a [routerLink]="['/employees', employee.id, 'exit']" class="btn btn--danger" *gggsHasPermission="'employee.exit.manage'">
            Record exit
          </a>
        }
        @if (employee.status === 'Exited' || employee.status === 'Absconded') {
          <a [routerLink]="['/employees', employee.id, 'rehire']" class="btn btn--primary" *gggsHasPermission="'employee.employment.manage'">
            Rehire
          </a>
        }
      </gggs-page-header>

      @if (actionError()) {
        <p class="field__error" role="alert">{{ actionError() }}</p>
      }

      <section class="panel">
        <h2 class="panel__heading">Profile</h2>
        <dl class="facts">
          <div class="facts__row">
            <dt>Employee code</dt>
            <dd>{{ employee.employeeCode }}</dd>
          </div>
          <div class="facts__row">
            <dt>Work email</dt>
            <dd>{{ employee.workEmail ?? '—' }}</dd>
          </div>
          <div class="facts__row">
            <dt>Date of joining</dt>
            <dd>{{ employee.dateOfJoining }}</dd>
          </div>
          @if (employee.originalHireDate) {
            <div class="facts__row">
              <dt>Original hire date</dt>
              <dd>{{ employee.originalHireDate }}</dd>
            </div>
          }
          @if (employee.confirmationDate) {
            <div class="facts__row">
              <dt>Confirmed on</dt>
              <dd>{{ employee.confirmationDate }}</dd>
            </div>
          }
          @if (employee.exitDate) {
            <div class="facts__row">
              <dt>Exit date</dt>
              <dd>{{ employee.exitDate }}</dd>
            </div>
          }
        </dl>
      </section>

      <section class="panel" *gggsHasPermission="'employee.history.view'">
        <h2 class="panel__heading">Employment history</h2>

        @if (history().length === 0) {
          <gggs-empty-state title="No employment history yet" />
        } @else {
          <table class="list-table">
            <thead>
              <tr>
                <th scope="col">From</th>
                <th scope="col">To</th>
                <th scope="col">Change</th>
                <th scope="col">Employment type</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              @for (record of history(); track record.id) {
                <tr [class.row--current]="record.isCurrent">
                  <td>{{ record.effectiveFrom }}</td>
                  <td>{{ record.effectiveTo ?? (record.isCurrent ? 'Current' : '—') }}</td>
                  <td>{{ record.changeType }}</td>
                  <td>{{ record.employmentType }}</td>
                  <td>{{ record.changeReason ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>

      <section class="panel" *gggsHasPermission="'document.file.view'">
        <h2 class="panel__heading">Documents</h2>

        <form
          class="upload"
          [formGroup]="uploadForm"
          *gggsHasPermission="'document.file.upload'"
          (ngSubmit)="upload(employee.id)"
        >
          <label class="field">
            <span class="field__label">Document type</span>
            <select formControlName="documentTypeCode">
              <option value="" disabled>Choose a type…</option>
              @for (type of documentTypes(); track type.id) {
                <option [value]="type.code">{{ type.name }}</option>
              }
            </select>
          </label>

          <label class="field">
            <span class="field__label">File</span>
            <input type="file" (change)="onFileSelected($event)" required />
          </label>

          @if (uploadError()) {
            <p class="field__error" role="alert">{{ uploadError() }}</p>
          }

          <button type="submit" class="btn btn--primary" [disabled]="isUploading() || !uploadFile()">
            {{ isUploading() ? 'Uploading…' : 'Upload' }}
          </button>
        </form>

        @if (isLoadingDocuments()) {
          <p>Loading…</p>
        } @else if (documents().length === 0) {
          <gggs-empty-state title="No documents yet" />
        } @else {
          <table class="list-table">
            <thead>
              <tr>
                <th scope="col">File</th>
                <th scope="col">Size</th>
                <th scope="col">Uploaded</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (document of documents(); track document.id) {
                <tr>
                  <td><a [href]="downloadUrl(document.id)" target="_blank" rel="noopener">{{ document.fileName }}</a></td>
                  <td>{{ formatSize(document.sizeBytes) }}</td>
                  <td>{{ document.createdDate | slice: 0 : 10 }}</td>
                  <td>
                    <button
                      type="button"
                      class="btn btn--danger"
                      *gggsHasPermission="'document.file.delete'"
                      [disabled]="isDeletingId() === document.id"
                      (click)="deleteDocument(document.id)"
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>
    } @else {
      <gggs-empty-state title="Employee not found" />
    }
  `,
  styles: `
    .panel {
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__heading {
      margin: 0 0 0.75rem;
      font-size: 1rem;
    }

    .facts {
      display: grid;
      gap: 0.5rem;
      margin: 0;
    }

    .facts__row {
      display: flex;
      gap: 0.5rem;
    }

    .facts__row dt {
      width: 10rem;
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .facts__row dd {
      margin: 0;
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

    .row--current {
      font-weight: 600;
    }

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .btn--danger {
      border-color: var(--gggs-color-danger, #b3261e);
      color: var(--gggs-color-danger, #b3261e);
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
      border-color: transparent;
      color: #fff;
    }

    .field__error {
      margin: 0 0 1rem;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .upload {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 0.75rem;
      margin-block-end: 1rem;
      padding-block-end: 1rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
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
  `,
})
export class EmployeeDetailPage {
  private readonly api = inject(EmployeesApi);
  private readonly documentsApi = inject(DocumentsApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly id = input.required<string>();

  protected readonly isLoading = signal(true);
  protected readonly employee = signal<Employee | null>(null);
  protected readonly history = signal<readonly EmploymentRecord[]>([]);
  protected readonly isActing = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly isLoadingDocuments = signal(true);
  protected readonly documents = signal<readonly DocumentItem[]>([]);
  protected readonly documentTypes = signal<readonly DocumentTypeItem[]>([]);
  protected readonly uploadFile = signal<File | null>(null);
  protected readonly isUploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly isDeletingId = signal<string | null>(null);

  protected readonly uploadForm = this.formBuilder.nonNullable.group({
    documentTypeCode: ['', Validators.required],
  });

  constructor() {
    void this.load();
    void this.loadDocuments();
  }

  protected subtitle(employee: Employee): string {
    return `${employee.employeeCode} · ${employee.status}`;
  }

  /** The one action this page performs in place - it takes no input, so a whole route would be
   * a page for a single confirm button. */
  protected async serveNotice(): Promise<void> {
    this.isActing.set(true);
    this.actionError.set(null);

    try {
      const employee = await this.api.serveNotice(this.id());
      this.employee.set(employee);
    } catch {
      this.actionError.set('This action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [employee, history] = await Promise.all([
        this.api.get(this.id()),
        this.api.getEmploymentHistory(this.id()),
      ]);

      this.employee.set(employee);
      this.history.set(history);
    } catch {
      this.employee.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected downloadUrl(documentId: string): string {
    return documentDownloadUrl(documentId);
  }

  protected formatSize(sizeBytes: number): string {
    if (sizeBytes < 1024) {
      return `${sizeBytes} B`;
    }

    const kilobytes = sizeBytes / 1024;
    return kilobytes < 1024 ? `${kilobytes.toFixed(1)} KB` : `${(kilobytes / 1024).toFixed(1)} MB`;
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFile.set(input.files?.[0] ?? null);
  }

  protected async upload(ownerId: string): Promise<void> {
    const file = this.uploadFile();

    if (this.uploadForm.invalid || !file || this.isUploading()) {
      this.uploadForm.markAllAsTouched();
      return;
    }

    this.isUploading.set(true);
    this.uploadError.set(null);

    try {
      const { documentTypeCode } = this.uploadForm.getRawValue();
      await this.documentsApi.upload({
        file,
        documentTypeCode,
        ownerType: 'Employee',
        ownerId,
      });

      this.uploadForm.reset({ documentTypeCode: '' });
      this.uploadFile.set(null);
      await this.loadDocuments();
    } catch (error) {
      this.uploadError.set(describeUploadError(error));
    } finally {
      this.isUploading.set(false);
    }
  }

  protected async deleteDocument(documentId: string): Promise<void> {
    this.isDeletingId.set(documentId);

    try {
      await this.documentsApi.delete(documentId);
      this.documents.update((current) => current.filter((document) => document.id !== documentId));
    } finally {
      this.isDeletingId.set(null);
    }
  }

  private async loadDocuments(): Promise<void> {
    this.isLoadingDocuments.set(true);

    try {
      const [documents, types] = await Promise.all([
        this.documentsApi.listByOwner('Employee', this.id()),
        this.documentsApi.listTypes(),
      ]);

      this.documents.set(documents);
      this.documentTypes.set(types.filter((type) => type.isEmployeeUploadable));
    } finally {
      this.isLoadingDocuments.set(false);
    }
  }
}

function describeUploadError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errorCode === 'DOCUMENT_EXTENSION_NOT_ALLOWED') {
      return 'That file type is not allowed for the chosen document type.';
    }

    if (error.errorCode === 'DOCUMENT_TOO_LARGE') {
      return 'That file is larger than the chosen document type allows.';
    }
  }

  return 'The file could not be uploaded. Please try again.';
}
