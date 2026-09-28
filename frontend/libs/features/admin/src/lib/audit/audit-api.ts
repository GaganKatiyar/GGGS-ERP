import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { AuditLogItem, AuditLogQuery, PagedResult } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** The tenant audit trail, over the gateway's `/api/v1/audit/logs` routes. Read-only: the only
 * writer is `AuditEntryConsumer` in `Gggs.Platform.Worker`, never this or any other API. */
@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  list(query: AuditLogQuery): Promise<PagedResult<AuditLogItem>> {
    const params: Record<string, string> = {
      page: String(query.page),
      pageSize: String(query.pageSize),
    };

    if (query.entityType) {
      params['entityType'] = query.entityType;
    }

    if (query.entityId) {
      params['entityId'] = query.entityId;
    }

    if (query.action) {
      params['action'] = query.action;
    }

    if (query.service) {
      params['service'] = query.service;
    }

    if (query.fromDate) {
      params['fromDate'] = query.fromDate;
    }

    if (query.toDate) {
      params['toDate'] = query.toDate;
    }

    return firstValueFrom(this.http.get<PagedResult<AuditLogItem>>('/api/v1/audit/logs', { params }));
  }
}
