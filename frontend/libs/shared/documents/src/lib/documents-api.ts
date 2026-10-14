import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  DocumentDetail,
  DocumentItem,
  DocumentSignature,
  DocumentTemplateItem,
  DocumentTypeItem,
  DocumentUpload,
  PagedResult,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export interface DocumentQuery {
  readonly ownerType?: 'Employee' | 'Organization';
  readonly ownerId?: string;
  readonly documentTypeId?: string;
  readonly search?: string;
  readonly expiringWithinDays?: number;
  readonly includeArchived?: boolean;
  readonly page?: number;
  readonly pageSize?: number;
}

/**
 * The Documents API (`/api/v1/documents`, `/api/v1/me/documents`). Downloads are plain links: the
 * session cookie authenticates a GET, so the browser saves the file itself.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/documents';

  types(includeInactive = false): Promise<readonly DocumentTypeItem[]> {
    return firstValueFrom(this.http.get<readonly DocumentTypeItem[]>(`${this.base}/types`, { params: { includeInactive } }));
  }

  createType(body: Omit<DocumentTypeItem, 'id' | 'isSystem' | 'isActive'>): Promise<DocumentTypeItem> {
    return firstValueFrom(this.http.post<DocumentTypeItem>(`${this.base}/types`, body));
  }

  updateType(id: string, body: Omit<DocumentTypeItem, 'id' | 'isSystem' | 'isActive'>): Promise<DocumentTypeItem> {
    return firstValueFrom(this.http.put<DocumentTypeItem>(`${this.base}/types/${id}`, body));
  }

  async setTypeActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/types/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  list(query: DocumentQuery): Promise<PagedResult<DocumentItem>> {
    const params: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params[key] = value as string | number | boolean;
      }
    }
    return firstValueFrom(this.http.get<PagedResult<DocumentItem>>(this.base, { params }));
  }

  get(id: string): Promise<DocumentDetail> {
    return firstValueFrom(this.http.get<DocumentDetail>(`${this.base}/${id}`));
  }

  upload(upload: DocumentUpload): Promise<DocumentDetail> {
    const form = new FormData();
    form.append('ownerType', upload.ownerType);
    if (upload.ownerId) {
      form.append('ownerId', upload.ownerId);
    }
    form.append('documentTypeId', upload.documentTypeId);
    form.append('title', upload.title);
    if (upload.expiresOn) {
      form.append('expiresOn', upload.expiresOn);
    }
    if (upload.employeeVisible !== undefined && upload.employeeVisible !== null) {
      form.append('employeeVisible', String(upload.employeeVisible));
    }
    if (upload.note) {
      form.append('note', upload.note);
    }
    form.append('file', upload.file, upload.file.name);
    return firstValueFrom(this.http.post<DocumentDetail>(this.base, form));
  }

  addVersion(id: string, file: File, note: string | null): Promise<DocumentDetail> {
    const form = new FormData();
    if (note) {
      form.append('note', note);
    }
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<DocumentDetail>(`${this.base}/${id}/versions`, form));
  }

  async setArchived(id: string, archived: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/${archived ? 'archive' : 'restore'}`, {}));
  }

  requestSignature(id: string, signerEmployeeId: string): Promise<DocumentSignature> {
    return firstValueFrom(this.http.post<DocumentSignature>(`${this.base}/${id}/signature-requests`, { signerEmployeeId }));
  }

  async cancelSignature(requestId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/signature-requests/${requestId}/cancel`, {}));
  }

  templates(): Promise<readonly DocumentTemplateItem[]> {
    return firstValueFrom(this.http.get<readonly DocumentTemplateItem[]>(`${this.base}/templates`));
  }

  createTemplate(body: Pick<DocumentTemplateItem, 'code' | 'name' | 'description' | 'documentTypeId' | 'body'>): Promise<DocumentTemplateItem> {
    return firstValueFrom(this.http.post<DocumentTemplateItem>(`${this.base}/templates`, body));
  }

  updateTemplate(id: string, body: Pick<DocumentTemplateItem, 'code' | 'name' | 'description' | 'documentTypeId' | 'body'>): Promise<DocumentTemplateItem> {
    return firstValueFrom(this.http.put<DocumentTemplateItem>(`${this.base}/templates/${id}`, body));
  }

  async setTemplateActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/templates/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  generate(templateId: string, employeeId: string, title: string | null, signerEmployeeId: string | null): Promise<DocumentDetail> {
    return firstValueFrom(this.http.post<DocumentDetail>(`${this.base}/templates/${templateId}/generate`, { employeeId, title, signerEmployeeId }));
  }

  // ----- Self-service ------------------------------------------------------------------------

  mine(): Promise<readonly DocumentItem[]> {
    return firstValueFrom(this.http.get<readonly DocumentItem[]>('/api/v1/me/documents'));
  }

  mySignatureRequests(): Promise<readonly DocumentSignature[]> {
    return firstValueFrom(this.http.get<readonly DocumentSignature[]>('/api/v1/me/signature-requests'));
  }

  async sign(requestId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/signature-requests/${requestId}/sign`, {}));
  }

  async decline(requestId: string, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/signature-requests/${requestId}/decline`, { comment }));
  }
}

/** Where a version's file is served from; a plain link the browser can follow. */
export function documentContentUrl(documentId: string, version: number, mine = false): string {
  return mine ? `/api/v1/me/documents/${documentId}/versions/${version}/content` : `/api/v1/documents/${documentId}/versions/${version}/content`;
}

/** "1.2 MB" for a byte count. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const units = ['KB', 'MB', 'GB'];
  let size = bytes / 1024;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  return `${size.toFixed(size >= 10 ? 0 : 1)} ${units[unit]}`;
}
