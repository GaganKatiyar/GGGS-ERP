import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AddHolidayRequest,
  CreateHolidayCalendarRequest,
  CreateLeavePolicyRequest,
  CreateLeavePolicyRuleRequest,
  CreateLeaveRequestRequest,
  CreateLeaveTypeRequest,
  HolidayCalendarListItem,
  HolidayListItem,
  LeaveBalance,
  LeavePolicyListItem,
  LeavePolicyRuleListItem,
  LeaveRequest,
  LeaveRequestListItem,
  LeaveTypeListItem,
  PagedResult,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Leave, over the gateway's `/api/v1/leaves/*` and `/api/v1/holidays/*` routes - mirrors
 * `LeaveEndpoints.cs` and `HolidayEndpoints.cs` on the server, the same one-service-per-module
 * shape as `OrgReferenceApi`.
 */
@Injectable({ providedIn: 'root' })
export class LeaveApi {
  private readonly http = inject(HttpClient);

  // ---- Leave types ----

  listLeaveTypes(): Promise<readonly LeaveTypeListItem[]> {
    return firstValueFrom(
      this.http.get<readonly LeaveTypeListItem[]>('/api/v1/leaves/types'),
    );
  }

  createLeaveType(request: CreateLeaveTypeRequest): Promise<LeaveTypeListItem> {
    return firstValueFrom(
      this.http.post<LeaveTypeListItem>('/api/v1/leaves/types', request),
    );
  }

  // ---- Leave policies ----

  listLeavePolicies(): Promise<readonly LeavePolicyListItem[]> {
    return firstValueFrom(
      this.http.get<readonly LeavePolicyListItem[]>('/api/v1/leaves/policies'),
    );
  }

  createLeavePolicy(request: CreateLeavePolicyRequest): Promise<LeavePolicyListItem> {
    return firstValueFrom(
      this.http.post<LeavePolicyListItem>('/api/v1/leaves/policies', request),
    );
  }

  listLeavePolicyRules(policyId: string): Promise<readonly LeavePolicyRuleListItem[]> {
    return firstValueFrom(
      this.http.get<readonly LeavePolicyRuleListItem[]>(
        `/api/v1/leaves/policies/${policyId}/rules`,
      ),
    );
  }

  createLeavePolicyRule(
    policyId: string,
    request: CreateLeavePolicyRuleRequest,
  ): Promise<LeavePolicyRuleListItem> {
    return firstValueFrom(
      this.http.post<LeavePolicyRuleListItem>(
        `/api/v1/leaves/policies/${policyId}/rules`,
        request,
      ),
    );
  }

  // ---- Balances ----

  getMyBalances(year?: number): Promise<readonly LeaveBalance[]> {
    return firstValueFrom(
      this.http.get<readonly LeaveBalance[]>('/api/v1/leaves/balances/me', {
        params: year === undefined ? {} : { year },
      }),
    );
  }

  getEmployeeBalances(
    employeeId: string,
    year?: number,
  ): Promise<readonly LeaveBalance[]> {
    return firstValueFrom(
      this.http.get<readonly LeaveBalance[]>(
        `/api/v1/leaves/balances/${employeeId}`,
        { params: year === undefined ? {} : { year } },
      ),
    );
  }

  // ---- Requests ----

  listRequests(query: {
    page: number;
    pageSize: number;
    employeeId?: string;
    status?: string;
  }): Promise<PagedResult<LeaveRequestListItem>> {
    return firstValueFrom(
      this.http.get<PagedResult<LeaveRequestListItem>>('/api/v1/leaves/requests', {
        params: toHttpParams(query),
      }),
    );
  }

  getRequest(id: string): Promise<LeaveRequest> {
    return firstValueFrom(
      this.http.get<LeaveRequest>(`/api/v1/leaves/requests/${id}`),
    );
  }

  submitRequest(request: CreateLeaveRequestRequest): Promise<LeaveRequest> {
    return firstValueFrom(
      this.http.post<LeaveRequest>('/api/v1/leaves/requests', request),
    );
  }

  cancelRequest(id: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/leaves/requests/${id}/cancel`, {}),
    );
  }

  // ---- Holidays ----

  listHolidayCalendars(): Promise<readonly HolidayCalendarListItem[]> {
    return firstValueFrom(
      this.http.get<readonly HolidayCalendarListItem[]>(
        '/api/v1/holidays/calendars',
      ),
    );
  }

  createHolidayCalendar(
    request: CreateHolidayCalendarRequest,
  ): Promise<HolidayCalendarListItem> {
    return firstValueFrom(
      this.http.post<HolidayCalendarListItem>('/api/v1/holidays/calendars', request),
    );
  }

  listHolidays(calendarId: string): Promise<readonly HolidayListItem[]> {
    return firstValueFrom(
      this.http.get<readonly HolidayListItem[]>(
        `/api/v1/holidays/calendars/${calendarId}/holidays`,
      ),
    );
  }

  addHoliday(calendarId: string, request: AddHolidayRequest): Promise<HolidayListItem> {
    return firstValueFrom(
      this.http.post<HolidayListItem>(
        `/api/v1/holidays/calendars/${calendarId}/holidays`,
        request,
      ),
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
