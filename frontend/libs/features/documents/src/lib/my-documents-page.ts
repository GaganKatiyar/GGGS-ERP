import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { DocumentsApi, documentContentUrl } from '@gggs/shared/documents';
import type { DocumentItem, DocumentSignature } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** My documents: what HR holds for me that I may see, and anything waiting for my signature. */
@Component({
  selector: 'gggs-my-documents-page',
  imports: [DatePipe, PageHeader, EmptyState, Skeleton, StatusBadge, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My documents" subtitle="Your letters, certificates and anything to sign." />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Your documents could not be shown" [description]="error()!" />
    } @else {
      <div class="stack">
        @if (toSign().length > 0) {
          <section class="panel stack">
            <h2 class="mine__heading">Waiting for your signature</h2>
            @for (request of toSign(); track request.id) {
              <div class="mine__row">
                <span class="mine__main">
                  <strong>{{ request.documentTitle }}</strong>
                  <span class="muted">version {{ request.versionNumber }} · requested {{ request.requestedDate | date: 'mediumDate' }}</span>
                </span>
                <a class="btn btn--sm" [href]="url(request.documentId, request.versionNumber)" download>Read</a>
                <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="sign(request)">Sign</button>
                <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="openDecline(request)">Decline</button>
              </div>
            }
          </section>
        }

        <section class="panel stack">
          <h2 class="mine__heading">Documents</h2>
          @for (document of documents(); track document.id) {
            <div class="mine__row">
              <span class="mine__main">
                <strong>{{ document.title }}</strong>
                <span class="muted">{{ document.typeName }} · v{{ document.currentVersion }}{{ document.expiresOn ? ' · expires ' + (document.expiresOn | date: 'mediumDate') : '' }}</span>
              </span>
              @if (document.isExpired) {
                <gggs-status-badge status="Expired" />
              }
              <a class="btn btn--sm" [href]="url(document.id, document.currentVersion)" download>Download</a>
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-folder-open" title="Nothing here yet" description="Letters and documents HR shares with you appear here." />
          }
        </section>

        @if (signed().length > 0) {
          <section class="panel stack">
            <h2 class="mine__heading">Signature history</h2>
            @for (request of signed(); track request.id) {
              <div class="mine__row">
                <span class="mine__main">{{ request.documentTitle }} <span class="muted">v{{ request.versionNumber }}</span></span>
                <gggs-status-badge [status]="request.status" />
                <span class="muted">{{ request.completedDate | date: 'mediumDate' }}</span>
              </div>
            }
          </section>
        }
        @if (actionError()) {
          <p class="field__error" role="alert">{{ actionError() }}</p>
        }
      </div>
    }

    <gggs-form-dialog [(visible)]="declineOpen" title="Decline to sign" saveLabel="Decline" [busy]="busy()" [error]="actionError()" (save)="decline()">
      <label class="field">
        <span class="field__label">Why</span>
        <textarea rows="3" maxlength="500" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .mine__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .mine__row {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .mine__main {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 0.125rem;
    }
  `,
})
export class MyDocumentsPage {
  private readonly api = inject(DocumentsApi);

  protected readonly documents = signal<readonly DocumentItem[]>([]);
  protected readonly requests = signal<readonly DocumentSignature[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly declineOpen = signal(false);
  protected readonly comment = signal('');
  private declining: DocumentSignature | null = null;

  protected readonly toSign = computed(() => this.requests().filter((request) => request.status === 'Requested'));
  protected readonly signed = computed(() => this.requests().filter((request) => request.status !== 'Requested'));

  constructor() {
    void this.load();
  }

  protected url(documentId: string, version: number): string {
    return documentContentUrl(documentId, version, true);
  }

  protected async sign(request: DocumentSignature): Promise<void> {
    await this.run(() => this.api.sign(request.id));
  }

  protected openDecline(request: DocumentSignature): void {
    this.declining = request;
    this.comment.set('');
    this.actionError.set(null);
    this.declineOpen.set(true);
  }

  protected async decline(): Promise<void> {
    const request = this.declining;
    if (!request) {
      return;
    }
    await this.run(async () => {
      await this.api.decline(request.id, this.comment().trim() || null);
      this.declineOpen.set(false);
    });
  }

  private async run(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await call();
      await this.load();
    } catch (error) {
      this.actionError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [documents, requests] = await Promise.all([this.api.mine(), this.api.mySignatureRequests()]);
      this.documents.set(documents);
      this.requests.set(requests);
    } catch (error) {
      this.error.set(
        error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'Try again in a moment.',
      );
    } finally {
      this.loading.set(false);
    }
  }
}
