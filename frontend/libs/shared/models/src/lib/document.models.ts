/**
 * Document types and uploaded files, mirroring the DTOs in
 * `Gggs.Documents.Api.Contracts.DocumentContracts`.
 */

export type DocumentOwnerType = 'Employee' | 'LegalEntity' | 'Department' | 'Tenant';

export type DocumentCategory = 'Employee' | 'Company' | 'Payroll' | 'Recruitment';

/**
 * Skipped is the only value this build ever actually sets - no malware scanner is wired yet
 * (see `Gggs.Documents.Domain.Documents.DocumentScanStatus`'s own remarks). The others are
 * declared so the UI already has a place to show a real result once one exists.
 */
export type DocumentScanStatus = 'Skipped' | 'Pending' | 'Clean' | 'Infected' | 'Failed';

export interface DocumentTypeItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly category: DocumentCategory;
  readonly allowedExtensions: readonly string[];
  readonly maxSizeMb: number;
  readonly isEmployeeUploadable: boolean;
  readonly isSensitive: boolean;
}

export interface CreateDocumentTypeRequest {
  readonly code: string;
  readonly name: string;
  readonly category: DocumentCategory;
  readonly allowedExtensions: readonly string[];
  readonly maxSizeMb: number;
  readonly isEmployeeUploadable: boolean;
  readonly isSensitive: boolean;
}

export interface DocumentItem {
  readonly id: string;
  readonly ownerType: DocumentOwnerType;
  readonly ownerId: string;
  readonly documentTypeId: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly sha256: string;
  readonly scanStatus: DocumentScanStatus;
  readonly uploadedByUserId: string;
  readonly expiryDate?: string;
  readonly createdDate: string;
}
