/**
 * The tenant audit trail, mirroring the DTOs in
 * `Gggs.Platform.Api.Endpoints.AuditEndpoints`.
 */

export interface AuditLogItem {
  readonly id: string;
  readonly occurredDate: string;
  readonly userId?: string;
  readonly service: string;
  readonly action: string;
  readonly entityType: string;
  readonly entityId: string;
  readonly oldValuesJson?: string;
  readonly newValuesJson?: string;
  readonly changedFieldsCsv?: string;
  readonly correlationId?: string;
}

export interface AuditLogQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly entityType?: string;
  readonly entityId?: string;
  readonly action?: string;
  readonly service?: string;
  readonly fromDate?: string;
  readonly toDate?: string;
}
