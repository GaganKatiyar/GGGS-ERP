import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { DocumentItem, DocumentTypeItem } from '@gggs/shared/models';
import { DOCUMENT_CATEGORY_LABELS } from '@gggs/shared/models';
import { EmptyState, FileUpload, FormDialog, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { DocumentsApi, documentContentUrl } from './documents-api';

const message = (error: unknown, fallback: string) => (error instanceof Error && error.message ? error.message : fallback);

/**
 * One employee's documents: what is held, each file's latest version to download, and - for those
 * allowed - uploading, new versions, archiving and asking for a signature. What the caller may do
 * is decided by the page (it holds the permissions), passed in as inputs.
 */
@Component({
  selector: 'gggs-employee-documents',
  imports: [DatePipe, ReactiveFormsModule, EmptyState, FileUpload, FormDialog, Skeleton, StatusBadge, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="documents__heading">Documents</h2>
        <label class="documents__archived"><input type="checkbox" [checked]="showArchived()" (change)="toggleArchived()" /> Show archived</label>
        @if (canUpload()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="openUpload()"><i class="pi pi-upload" aria-hidden="true"></i> Upload</button>
        }
      </div>

      @if (loading()) {
        <gggs-skeleton [lines]="4" />
      } @else if (documents().length === 0) {
        <gggs-empty-state icon="pi pi-folder-open" title="No documents yet" [description]="canUpload() ? 'Upload identity proofs, certificates and letters here.' : undefined" />
      } @else {
        <table class="documents__table">
          <thead>
            <tr>
              <th scope="col">Document</th>
              <th scope="col">Type</th>
              <th scope="col">Version</th>
              <th scope="col">Expires</th>
              <th scope="col">Employee sees it</th>
              <th scope="col"><span class="documents__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (document of documents(); track document.id) {
              <tr [class.documents__row--archived]="!document.isActive">
                <td>
                  <strong>{{ document.title }}</strong>
                  @if (document.source === 'Generated') {
                    <span class="muted documents__note">generated</span>
                  }
                </td>
                <td>{{ document.typeName }} <span class="muted">· {{ categoryLabel(document) }}</span></td>
                <td>v{{ document.currentVersion }}</td>
                <td>
                  @if (document.expiresOn) {
                    @if (document.isExpired) {
                      <gggs-status-badge status="Expired" />
                    } @else {
                      {{ document.expiresOn | date: 'mediumDate' }}
                    }
                  } @else {
                    —
                  }
                </td>
                <td>{{ document.employeeVisible ? 'Yes' : 'No' }}</td>
                <td class="documents__actions">
                  <a class="btn btn--link btn--sm" [href]="contentUrl(document)" download>Download</a>
                  @if (canUpload() && document.isActive) {
                    <button type="button" class="btn btn--link btn--sm" (click)="openVersion(document)">New version</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="openSignature(document)">Ask to sign</button>
                  }
                  @if (canArchive()) {
                    <button type="button" class="btn btn--link btn--sm" (click)="setArchived(document, document.isActive)">{{ document.isActive ? 'Archive' : 'Restore' }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
      @if (listError()) {
        <p class="field__error" role="alert">{{ listError() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="uploadOpen" title="Upload a document" saveLabel="Upload" [busy]="busy()" [error]="error()" (save)="upload()">
      <div class="form-grid" [formGroup]="uploadForm">
        <label class="field">
          <span class="field__label">Type *</span>
          <select formControlName="documentTypeId" (change)="onTypeChange()">
            <option value="">Choose a type</option>
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Title *</span>
          <input type="text" formControlName="title" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Expires{{ selectedType()?.requiresExpiry ? ' *' : '' }}</span>
          <input type="date" formControlName="expiresOn" />
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="employeeVisible" /> The employee can see it</span>
        </label>
        <div class="field form-grid__full">
          <span class="field__label">File * <span class="muted">PDF, image, Office document, text or CSV; up to 10 MB</span></span>
          <gggs-file-upload accept=".pdf,.png,.jpg,.jpeg,.webp,.doc,.docx,.xls,.xlsx,.txt,.csv" (fileSelected)="file.set($event)" />
        </div>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="versionOpen" [title]="'New version of ' + (target()?.title ?? '')" saveLabel="Upload" [busy]="busy()" [error]="error()" (save)="addVersion()">
      <div class="stack">
        <gggs-file-upload accept=".pdf,.png,.jpg,.jpeg,.webp,.doc,.docx,.xls,.xlsx,.txt,.csv" (fileSelected)="file.set($event)" />
        <label class="field">
          <span class="field__label">What changed</span>
          <input type="text" maxlength="500" [value]="versionNote()" (input)="versionNote.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="signatureOpen" title="Ask someone to sign" saveLabel="Send request" [busy]="busy()" [error]="error()" (save)="requestSignature()">
      <div class="stack">
        <p class="muted">They sign in PeopleOS; the time and address are recorded with the signature.</p>
        <label class="field">
          <span class="field__label">Signer *</span>
          <gggs-employee-select [formControl]="signer" ariaLabel="Signer" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .documents__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .documents__archived {
      font-size: var(--gggs-font-size-sm);
    }

    .documents__table {
      width: 100%;
      border-collapse: collapse;
    }

    .documents__table th,
    .documents__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
      vertical-align: top;
    }

    .documents__row--archived {
      opacity: 0.6;
    }

    .documents__note {
      margin-inline-start: var(--gggs-space-2);
      font-size: var(--gggs-font-size-xs);
    }

    .documents__actions {
      white-space: nowrap;
    }

    .documents__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class EmployeeDocuments {
  private readonly api = inject(DocumentsApi);

  readonly employeeId = input.required<string>();
  readonly canUpload = input(false);
  readonly canArchive = input(false);

  protected readonly documents = signal<readonly DocumentItem[]>([]);
  protected readonly types = signal<readonly DocumentTypeItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly showArchived = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly target = signal<DocumentItem | null>(null);
  protected readonly versionNote = signal('');
  protected readonly uploadOpen = signal(false);
  protected readonly versionOpen = signal(false);
  protected readonly signatureOpen = signal(false);

  protected readonly uploadForm = new FormGroup({
    documentTypeId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    title: new FormControl('', { nonNullable: true, validators: Validators.required }),
    expiresOn: new FormControl<string | null>(null),
    employeeVisible: new FormControl(true, { nonNullable: true }),
  });
  protected readonly signer = new FormControl<string | null>(null);

  protected readonly selectedType = computed(() => this.types().find((type) => type.id === this.typeId()));
  private readonly typeId = signal('');

  constructor() {
    effect(() => {
      const id = this.employeeId();
      this.showArchived();
      void this.load(id);
    });
  }

  protected categoryLabel(document: DocumentItem): string {
    return DOCUMENT_CATEGORY_LABELS[document.category];
  }

  protected contentUrl(document: DocumentItem): string {
    return documentContentUrl(document.id, document.currentVersion);
  }

  protected toggleArchived(): void {
    this.showArchived.update((value) => !value);
  }

  protected async openUpload(): Promise<void> {
    if (this.types().length === 0) {
      this.types.set(await this.api.types());
    }
    this.uploadForm.reset({ documentTypeId: '', title: '', expiresOn: null, employeeVisible: true });
    this.typeId.set('');
    this.file.set(null);
    this.error.set(null);
    this.uploadOpen.set(true);
  }

  protected onTypeChange(): void {
    const type = this.types().find((candidate) => candidate.id === this.uploadForm.controls.documentTypeId.value);
    this.typeId.set(type?.id ?? '');
    if (type) {
      this.uploadForm.patchValue({ employeeVisible: type.employeeVisible, title: this.uploadForm.controls.title.value || type.name });
    }
  }

  protected openVersion(document: DocumentItem): void {
    this.target.set(document);
    this.file.set(null);
    this.versionNote.set('');
    this.error.set(null);
    this.versionOpen.set(true);
  }

  protected openSignature(document: DocumentItem): void {
    this.target.set(document);
    this.signer.setValue(this.employeeId());
    this.error.set(null);
    this.signatureOpen.set(true);
  }

  protected async upload(): Promise<void> {
    const file = this.file();
    const value = this.uploadForm.getRawValue();
    if (this.uploadForm.invalid || !file) {
      this.error.set('Choose a type, give it a title and pick a file.');
      return;
    }

    await this.run(async () => {
      await this.api.upload({
        ownerType: 'Employee',
        ownerId: this.employeeId(),
        documentTypeId: value.documentTypeId,
        title: value.title.trim(),
        expiresOn: value.expiresOn || null,
        employeeVisible: value.employeeVisible,
        file,
      });
      this.uploadOpen.set(false);
    });
  }

  protected async addVersion(): Promise<void> {
    const file = this.file();
    const target = this.target();
    if (!file || !target) {
      this.error.set('Pick the new file.');
      return;
    }

    await this.run(async () => {
      await this.api.addVersion(target.id, file, this.versionNote().trim() || null);
      this.versionOpen.set(false);
    });
  }

  protected async requestSignature(): Promise<void> {
    const target = this.target();
    const signer = this.signer.value;
    if (!target || !signer) {
      this.error.set('Choose who should sign.');
      return;
    }

    await this.run(async () => {
      await this.api.requestSignature(target.id, signer);
      this.signatureOpen.set(false);
    });
  }

  protected async setArchived(document: DocumentItem, archived: boolean): Promise<void> {
    this.listError.set(null);
    try {
      await this.api.setArchived(document.id, archived);
      await this.load(this.employeeId());
    } catch (error) {
      this.listError.set(message(error, 'The document could not be changed.'));
    }
  }

  private async run(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
      await this.load(this.employeeId());
    } catch (error) {
      this.error.set(message(error, 'That did not work. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(employeeId: string): Promise<void> {
    this.loading.set(true);
    this.listError.set(null);
    try {
      const page = await this.api.list({ ownerType: 'Employee', ownerId: employeeId, includeArchived: this.showArchived(), pageSize: 200 });
      this.documents.set(page.items);
    } catch (error) {
      this.documents.set([]);
      this.listError.set(message(error, 'Documents could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }
}
