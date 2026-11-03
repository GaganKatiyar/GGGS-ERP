import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DocumentsApi, documentContentUrl } from '@gggs/shared/documents';
import type { DocumentItem } from '@gggs/shared/models';
import { EmptyState, Skeleton } from '@gggs/shared/ui';

/** The employee's own documents on My profile, to open in place; signing and requests live on My documents. */
@Component({
  selector: 'gggs-my-documents-tab',
  imports: [DatePipe, RouterLink, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    } @else if (documents(); as list) {
      <div class="bg-bar">
        <a class="btn btn--sm" routerLink="/me/documents">All my documents <i class="pi pi-arrow-right" aria-hidden="true"></i></a>
      </div>
      <ul class="bg-list" aria-label="My documents">
        @for (document of list; track document.id) {
          <li class="bg-card">
            <span class="bg-card__icon" aria-hidden="true"><i class="pi pi-file"></i></span>
            <span class="bg-card__body">
              <strong>{{ document.title }}</strong>
              <span class="muted">
                {{ document.typeName }} · version {{ document.currentVersion }}
                @if (document.expiresOn) {
                  · {{ document.isExpired ? 'expired' : 'expires' }} {{ document.expiresOn | date: 'mediumDate' }}
                }
              </span>
            </span>
            <span class="bg-card__actions">
              <a class="btn btn--link btn--sm" [href]="url(document)" target="_blank" rel="noopener" [attr.aria-label]="'Open ' + document.title">Open</a>
            </span>
          </li>
        } @empty {
          <li><gggs-empty-state icon="pi pi-file" title="No documents yet" description="Letters and documents HR shares with you will appear here." /></li>
        }
      </ul>
    } @else {
      <gggs-skeleton [lines]="4" />
    }
  `,
})
export class MyDocumentsTab {
  protected readonly documents = signal<readonly DocumentItem[] | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(DocumentsApi).mine().then(
      (documents) => this.documents.set(documents),
      (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'Your documents could not be loaded.'),
    );
  }

  protected url(document: DocumentItem): string {
    return documentContentUrl(document.id, document.currentVersion, true);
  }
}
