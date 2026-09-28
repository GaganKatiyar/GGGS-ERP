import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  CreateDocumentTypeRequest,
  DocumentItem,
  DocumentOwnerType,
  DocumentTypeItem,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Documents and document types, over the gateway's `/api/v1/documents` routes.
 *
 * Downloading is deliberately not a method here: the endpoint either redirects to a signed link
 * or streams the bytes, and both work correctly as a plain browser navigation (the BFF session
 * cookie travels with it automatically) - fetching it through `HttpClient` and buffering a blob
 * would only add work and a memory cost for no benefit. See {@link documentDownloadUrl}.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  listByOwner(ownerType: DocumentOwnerType, ownerId: string): Promise<readonly DocumentItem[]> {
    return firstValueFrom(
      this.http.get<readonly DocumentItem[]>('/api/v1/documents', {
        params: { ownerType, ownerId },
      }),
    );
  }

  upload(request: {
    file: File;
    documentTypeCode: string;
    ownerType: DocumentOwnerType;
    ownerId: string;
    expiryDate?: string;
  }): Promise<DocumentItem> {
    const form = new FormData();
    form.append('file', request.file, request.file.name);
    form.append('documentTypeCode', request.documentTypeCode);
    form.append('ownerType', request.ownerType);
    form.append('ownerId', request.ownerId);

    if (request.expiryDate) {
      form.append('expiryDate', request.expiryDate);
    }

    return firstValueFrom(this.http.post<DocumentItem>('/api/v1/documents', form));
  }

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/documents/${id}`));
  }

  listTypes(): Promise<readonly DocumentTypeItem[]> {
    return firstValueFrom(this.http.get<readonly DocumentTypeItem[]>('/api/v1/documents/types'));
  }

  createType(request: CreateDocumentTypeRequest): Promise<DocumentTypeItem> {
    return firstValueFrom(
      this.http.post<DocumentTypeItem>('/api/v1/documents/types', request),
    );
  }
}

/** A plain link, not an `HttpClient` call - see {@link DocumentsApi}'s own remarks on why. */
export function documentDownloadUrl(id: string): string {
  return `/api/v1/documents/${id}/download`;
}
