import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import type { DocumentCategory, DocumentItem, DocumentTypeItem } from '@gggs/shared/models';
import { EmptyState, FileUpload, FormDialog, Skeleton } from '@gggs/shared/ui';
import { DocumentsApi, documentContentUrl } from './documents-api';

const message = (error: unknown, fallback: string) => (error instanceof Error && error.message ? error.message : fallback);

/**
 * The documents held for a client or a project (spec §55): the SOW, BRD, designs, UAT sign-off.
 * Who may read and add them is the owning module's rule; the server enforces it and the page says
 * whether to offer uploads.
 */
@Component({
  selector: 'gggs-record-documents',
  imports: [DatePipe, EmptyState, FileUpload, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="record-docs__heading">Documents</h2>
        @if (canUpload()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="openUpload()"><i class="pi pi-upload" aria-hidden="true"></i> Upload</button>
        }
      </div>

      @if (loading()) {
        <gggs-skeleton [lines]="3" />
      } @else if (documents().length === 0) {
        <gggs-empty-state icon="pi pi-folder-open" title="No documents yet" [description]="canUpload() ? emptyHint() : undefined" />
      } @else {
        <table class="record-docs__table">
          <thead>
            <tr>
              <th scope="col">Document</th>
              <th scope="col">Type</th>
              <th scope="col">Version</th>
              <th scope="col">Updated</th>
              <th scope="col"><span class="record-docs__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (document of documents(); track document.id) {
              <tr>
                <td><strong>{{ document.title }}</strong></td>
                <td>{{ document.typeName }}</td>
                <td>v{{ document.currentVersion }}</td>
                <td>{{ document.modifiedDate ?? document.createdDate | date: 'mediumDate' }}</td>
                <td class="record-docs__actions">
                  <a class="btn btn--link btn--sm" [href]="contentUrl(document)" download>Download</a>
                  @if (canUpload()) {
                    <button type="button" class="btn btn--link btn--sm" (click)="openVersion(document)">New version</button>
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
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Type *</span>
          <select [value]="typeId()" (change)="onType($any($event.target).value)">
            <option value="">Choose a type</option>
            @for (type of offered(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="200" [value]="title()" (input)="title.set($any($event.target).value)" />
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
          <input type="text" maxlength="500" [value]="note()" (input)="note.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .record-docs__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .record-docs__table {
      width: 100%;
      border-collapse: collapse;
    }

    .record-docs__table th,
    .record-docs__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .record-docs__actions {
      white-space: nowrap;
    }

    .record-docs__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class RecordDocuments {
  private readonly api = inject(DocumentsApi);

  readonly ownerType = input.required<'Client' | 'Project'>();
  readonly ownerId = input.required<string>();
  readonly canUpload = input(false);

  /** Document categories offered first in the type list; the rest follow. */
  readonly categories = input<readonly DocumentCategory[]>(['Project', 'Contract', 'Other']);

  protected readonly documents = signal<readonly DocumentItem[]>([]);
  protected readonly types = signal<readonly DocumentTypeItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly target = signal<DocumentItem | null>(null);
  protected readonly typeId = signal('');
  protected readonly title = signal('');
  protected readonly note = signal('');
  protected readonly uploadOpen = signal(false);
  protected readonly versionOpen = signal(false);

  protected readonly offered = computed(() => {
    const wanted = this.categories();
    return this.types().filter((type) => wanted.includes(type.category));
  });

  protected readonly emptyHint = computed(() =>
    this.ownerType() === 'Project' ? 'Keep the SOW, requirements, designs, test plans and sign-offs here.' : 'Keep signed agreements, NDAs and purchase orders here.',
  );

  constructor() {
    effect(() => {
      const owner = this.ownerId();
      void this.load(owner);
    });
  }

  protected contentUrl(document: DocumentItem): string {
    return documentContentUrl(document.id, document.currentVersion);
  }

  protected async openUpload(): Promise<void> {
    if (this.types().length === 0) {
      this.types.set(await this.api.types());
    }
    this.typeId.set('');
    this.title.set('');
    this.file.set(null);
    this.error.set(null);
    this.uploadOpen.set(true);
  }

  protected onType(id: string): void {
    this.typeId.set(id);
    const type = this.types().find((candidate) => candidate.id === id);
    if (type && !this.title()) {
      this.title.set(type.name);
    }
  }

  protected openVersion(document: DocumentItem): void {
    this.target.set(document);
    this.file.set(null);
    this.note.set('');
    this.error.set(null);
    this.versionOpen.set(true);
  }

  protected async upload(): Promise<void> {
    const file = this.file();
    if (!this.typeId() || !this.title().trim() || !file) {
      this.error.set('Choose a type, give it a title and pick a file.');
      return;
    }

    await this.run(async () => {
      await this.api.upload({ ownerType: this.ownerType(), ownerId: this.ownerId(), documentTypeId: this.typeId(), title: this.title().trim(), file });
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
      await this.api.addVersion(target.id, file, this.note().trim() || null);
      this.versionOpen.set(false);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load(this.ownerId());
    } catch (error) {
      this.error.set(message(error, 'That did not work. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(ownerId: string): Promise<void> {
    this.loading.set(true);
    this.listError.set(null);
    try {
      const page = await this.api.list({ ownerType: this.ownerType(), ownerId, pageSize: 100 });
      this.documents.set(page.items);
    } catch (error) {
      this.listError.set(message(error, 'Documents could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }
}
