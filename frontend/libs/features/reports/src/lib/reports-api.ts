import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AttendanceSummaryReport,
  HeadcountReport,
  LeaveSummaryReport,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Reports, over the gateway's `/api/v1/reports/*` routes - one client for all three v1 reports,
 * even though each is served by a different owning service (Core HR for headcount, Time for
 * attendance/leave), mirroring `LeaveApi`'s own "one service per module" shape rather than one
 * client per backend service.
 */
@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  getHeadcount(fromDate?: string, toDate?: string): Promise<HeadcountReport> {
    return firstValueFrom(
      this.http.get<HeadcountReport>('/api/v1/reports/headcount', {
        params: toHttpParams({ fromDate, toDate }),
      }),
    );
  }

  getAttendanceSummary(fromDate?: string, toDate?: string): Promise<AttendanceSummaryReport> {
    return firstValueFrom(
      this.http.get<AttendanceSummaryReport>('/api/v1/reports/attendance-summary', {
        params: toHttpParams({ fromDate, toDate }),
      }),
    );
  }

  getLeaveSummary(periodYear?: number): Promise<LeaveSummaryReport> {
    return firstValueFrom(
      this.http.get<LeaveSummaryReport>('/api/v1/reports/leave-summary', {
        params: toHttpParams({ periodYear }),
      }),
    );
  }
}

/** `HttpParams` refuses `undefined`, so this drops anything the caller left unset. */
function toHttpParams(
  query: Record<string, string | number | boolean | undefined>,
): Record<string, string> {
  const params: Record<string, string> = {};

  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params[key] = String(value);
    }
  }

  return params;
}
