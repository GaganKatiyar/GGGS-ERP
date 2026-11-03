import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AttendanceDay,
  AttendanceDevice,
  AttendanceSummary,
  Holiday,
  HolidayCalendar,
  HolidayEntry,
  LeaveApplication,
  LeaveAttachment,
  EmployeeLeavePlan,
  EmployeeLeavePlanInput,
  LeaveBalance,
  LeavePlanAssignment,
  LeavePlanDetail,
  LeavePlanInput,
  LeavePlanItemInput,
  LeavePlanSummary,
  LeavePreview,
  LeaveRequest,
  LeaveRequestStatus,
  LeaveType,
  LedgerEntry,
  PagedResult,
  ResolvedLeavePolicy,
  Regularization,
  RosterRow,
  ScheduledDay,
  ShiftAssignment,
  ShiftSwap,
  TodayAttendance,
  WorkShift,
  WorkShiftInput,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

function params(query: object): Record<string, string | number | boolean> {
  const result: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = value as string | number | boolean;
    }
  }
  return result;
}

/** Today's date as the browser sees it, "yyyy-MM-dd". */
export function todayIso(): string {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

/** A date string moved by some days, "yyyy-MM-dd" in and out. */
export function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** Where a leave request's document is served from; a plain link the browser can follow. */
export function leaveAttachmentUrl(requestId: string, attachmentId: string): string {
  return `/api/v1/leave/requests/${requestId}/attachments/${attachmentId}/content`;
}

/** The Leave API (`/api/v1/leave`, `/api/v1/me/leave`). */
@Injectable({ providedIn: 'root' })
export class LeaveApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/leave';

  types(includeInactive = false): Promise<readonly LeaveType[]> {
    return firstValueFrom(this.http.get<readonly LeaveType[]>(`${this.base}/types`, { params: { includeInactive } }));
  }

  saveType(id: string | null, body: Pick<LeaveType, 'code' | 'name' | 'isPaid' | 'allowHalfDay' | 'color'>): Promise<LeaveType> {
    return firstValueFrom(id ? this.http.put<LeaveType>(`${this.base}/types/${id}`, body) : this.http.post<LeaveType>(`${this.base}/types`, body));
  }

  async setTypeActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/types/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  // ----- Leave policies: named sets of leave types and rules, assigned per engagement type -----

  plans(): Promise<readonly LeavePlanSummary[]> {
    return firstValueFrom(this.http.get<readonly LeavePlanSummary[]>(`${this.base}/plans`));
  }

  plan(id: string): Promise<LeavePlanDetail> {
    return firstValueFrom(this.http.get<LeavePlanDetail>(`${this.base}/plans/${id}`));
  }

  savePlan(id: string | null, body: LeavePlanInput): Promise<LeavePlanDetail> {
    return firstValueFrom(id ? this.http.put<LeavePlanDetail>(`${this.base}/plans/${id}`, body) : this.http.post<LeavePlanDetail>(`${this.base}/plans`, body));
  }

  /** Replaces the policy's leave types and their rules in one save. */
  setPlanItems(id: string, items: readonly LeavePlanItemInput[]): Promise<LeavePlanDetail> {
    return firstValueFrom(this.http.put<LeavePlanDetail>(`${this.base}/plans/${id}/items`, items));
  }

  copyPlan(id: string): Promise<LeavePlanDetail> {
    return firstValueFrom(this.http.post<LeavePlanDetail>(`${this.base}/plans/${id}/copy`, {}));
  }

  async setPlanActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/plans/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  assignments(): Promise<readonly LeavePlanAssignment[]> {
    return firstValueFrom(this.http.get<readonly LeavePlanAssignment[]>(`${this.base}/plan-assignments`));
  }

  saveAssignments(changes: readonly { employmentTypeCode: string; planId: string | null }[]): Promise<readonly LeavePlanAssignment[]> {
    return firstValueFrom(this.http.put<readonly LeavePlanAssignment[]>(`${this.base}/plan-assignments`, changes));
  }

  exceptions(employeeId?: string): Promise<readonly EmployeeLeavePlan[]> {
    return firstValueFrom(this.http.get<readonly EmployeeLeavePlan[]>(`${this.base}/employee-plans`, { params: params({ employeeId }) }));
  }

  async saveException(id: string | null, body: EmployeeLeavePlanInput): Promise<void> {
    await firstValueFrom(id ? this.http.put(`${this.base}/employee-plans/${id}`, body) : this.http.post(`${this.base}/employee-plans`, body));
  }

  async removeException(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/employee-plans/${id}`));
  }

  /** The policy an employee follows today; null when none is assigned. */
  resolve(employeeId: string): Promise<ResolvedLeavePolicy | null> {
    return firstValueFrom(this.http.get<ResolvedLeavePolicy | null>(`${this.base}/plans/resolve`, { params: { employeeId } }));
  }

  myPolicy(): Promise<ResolvedLeavePolicy | null> {
    return firstValueFrom(this.http.get<ResolvedLeavePolicy | null>('/api/v1/me/leave/policy'));
  }

  runAccrual(year: number, month: number): Promise<{ period: string; accrued: number; lapsed: number }> {
    return firstValueFrom(this.http.post<{ period: string; accrued: number; lapsed: number }>(`${this.base}/accrual/run`, { year, month }));
  }

  calendars(): Promise<readonly HolidayCalendar[]> {
    return firstValueFrom(this.http.get<readonly HolidayCalendar[]>(`${this.base}/holiday-calendars`));
  }

  saveCalendar(id: string | null, body: { code: string; name: string; isDefault: boolean; locationIds: readonly string[] }): Promise<HolidayCalendar> {
    return firstValueFrom(
      id ? this.http.put<HolidayCalendar>(`${this.base}/holiday-calendars/${id}`, body) : this.http.post<HolidayCalendar>(`${this.base}/holiday-calendars`, body),
    );
  }

  holidays(calendarId: string, year: number): Promise<readonly Holiday[]> {
    return firstValueFrom(this.http.get<readonly Holiday[]>(`${this.base}/holiday-calendars/${calendarId}/holidays`, { params: { year } }));
  }

  addHoliday(calendarId: string, body: { date: string; name: string; isOptional: boolean }): Promise<Holiday> {
    return firstValueFrom(this.http.post<Holiday>(`${this.base}/holiday-calendars/${calendarId}/holidays`, body));
  }

  async removeHoliday(calendarId: string, holidayId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/holiday-calendars/${calendarId}/holidays/${holidayId}`));
  }

  requests(query: { employeeId?: string; status?: LeaveRequestStatus | ''; from?: string; to?: string; page?: number; pageSize?: number }): Promise<PagedResult<LeaveRequest>> {
    return firstValueFrom(this.http.get<PagedResult<LeaveRequest>>(`${this.base}/requests`, { params: params(query) }));
  }

  applyOnBehalf(body: LeaveApplication): Promise<LeaveRequest> {
    return firstValueFrom(this.http.post<LeaveRequest>(`${this.base}/requests`, body));
  }

  previewOnBehalf(body: LeaveApplication): Promise<LeavePreview> {
    return firstValueFrom(this.http.post<LeavePreview>(`${this.base}/requests/preview`, body));
  }

  async cancel(id: string, reason: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/requests/${id}/cancel`, { reason }));
  }

  calendar(from: string, to: string, departmentId?: string | null): Promise<readonly LeaveRequest[]> {
    return firstValueFrom(this.http.get<readonly LeaveRequest[]>(`${this.base}/calendar`, { params: params({ from, to, departmentId }) }));
  }

  balances(employeeId: string): Promise<readonly LeaveBalance[]> {
    return firstValueFrom(this.http.get<readonly LeaveBalance[]>(`${this.base}/balances`, { params: { employeeId } }));
  }

  ledger(employeeId: string, leaveTypeId?: string): Promise<readonly LedgerEntry[]> {
    return firstValueFrom(this.http.get<readonly LedgerEntry[]>(`${this.base}/ledger`, { params: params({ employeeId, leaveTypeId }) }));
  }

  async adjust(body: { employeeId: string; leaveTypeId: string; days: number; note: string }): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/adjustments`, body));
  }

  myBalances(): Promise<readonly LeaveBalance[]> {
    return firstValueFrom(this.http.get<readonly LeaveBalance[]>('/api/v1/me/leave/balances'));
  }

  myRequests(page = 1, pageSize = 50): Promise<PagedResult<LeaveRequest>> {
    return firstValueFrom(this.http.get<PagedResult<LeaveRequest>>('/api/v1/me/leave/requests', { params: { page, pageSize } }));
  }

  apply(body: LeaveApplication): Promise<LeaveRequest> {
    return firstValueFrom(this.http.post<LeaveRequest>('/api/v1/me/leave/requests', body));
  }

  preview(body: LeaveApplication): Promise<LeavePreview> {
    return firstValueFrom(this.http.post<LeavePreview>('/api/v1/me/leave/requests/preview', body));
  }

  /** Adds a supporting document (PDF, JPEG or PNG, up to 5 MB) to one of my requests. */
  attach(requestId: string, file: File): Promise<LeaveAttachment> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<LeaveAttachment>(`/api/v1/me/leave/requests/${requestId}/attachments`, form));
  }

  /** Sends a draft, now with its document, for approval. */
  submit(requestId: string): Promise<LeaveRequest> {
    return firstValueFrom(this.http.post<LeaveRequest>(`/api/v1/me/leave/requests/${requestId}/submit`, {}));
  }

  attachments(requestId: string): Promise<readonly LeaveAttachment[]> {
    return firstValueFrom(this.http.get<readonly LeaveAttachment[]>(`${this.base}/requests/${requestId}/attachments`));
  }

  async cancelMine(id: string, reason: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/leave/requests/${id}/cancel`, { reason }));
  }

  myHolidays(year: number): Promise<readonly HolidayEntry[]> {
    return firstValueFrom(this.http.get<readonly HolidayEntry[]>('/api/v1/me/holidays', { params: { year } }));
  }
}

/** The Attendance API (`/api/v1/attendance`, `/api/v1/me/attendance`). */
@Injectable({ providedIn: 'root' })
export class AttendanceApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/attendance';

  days(query: { from: string; to: string; departmentId?: string | null; employeeId?: string | null }): Promise<readonly AttendanceDay[]> {
    return firstValueFrom(this.http.get<readonly AttendanceDay[]>(`${this.base}/days`, { params: params(query) }));
  }

  summary(date?: string, departmentId?: string | null): Promise<AttendanceSummary> {
    return firstValueFrom(this.http.get<AttendanceSummary>(`${this.base}/summary`, { params: params({ date, departmentId }) }));
  }

  recompute(body: { from: string; to: string; departmentId?: string | null; employeeId?: string | null }): Promise<{ days: number }> {
    return firstValueFrom(this.http.post<{ days: number }>(`${this.base}/recompute`, body));
  }

  regularizations(status?: string): Promise<readonly Regularization[]> {
    return firstValueFrom(this.http.get<readonly Regularization[]>(`${this.base}/regularizations`, { params: params({ status }) }));
  }

  devices(): Promise<readonly AttendanceDevice[]> {
    return firstValueFrom(this.http.get<readonly AttendanceDevice[]>(`${this.base}/devices`));
  }

  saveDevice(id: string | null, body: { code: string; name: string; locationId?: string | null }): Promise<AttendanceDevice> {
    return firstValueFrom(id ? this.http.put<AttendanceDevice>(`${this.base}/devices/${id}`, body) : this.http.post<AttendanceDevice>(`${this.base}/devices`, body));
  }

  async setDeviceActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/devices/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  today(): Promise<TodayAttendance> {
    return firstValueFrom(this.http.get<TodayAttendance>('/api/v1/me/attendance/today'));
  }

  punch(): Promise<TodayAttendance> {
    return firstValueFrom(this.http.post<TodayAttendance>('/api/v1/me/attendance/punch', {}));
  }

  myDays(from: string, to: string): Promise<readonly AttendanceDay[]> {
    return firstValueFrom(this.http.get<readonly AttendanceDay[]>('/api/v1/me/attendance', { params: { from, to } }));
  }

  myRegularizations(): Promise<readonly Regularization[]> {
    return firstValueFrom(this.http.get<readonly Regularization[]>('/api/v1/me/attendance/regularizations'));
  }

  requestRegularization(body: { date: string; checkIn: string | null; checkOut: string | null; reason: string }): Promise<Regularization> {
    return firstValueFrom(this.http.post<Regularization>('/api/v1/me/attendance/regularizations', body));
  }

  async cancelRegularization(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/attendance/regularizations/${id}/cancel`, {}));
  }
}

/** The Shift API (`/api/v1/shifts`, `/api/v1/me/shifts`). */
@Injectable({ providedIn: 'root' })
export class ShiftsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/shifts';

  list(): Promise<readonly WorkShift[]> {
    return firstValueFrom(this.http.get<readonly WorkShift[]>(this.base));
  }

  save(id: string | null, body: WorkShiftInput): Promise<WorkShift> {
    return firstValueFrom(id ? this.http.put<WorkShift>(`${this.base}/${id}`, body) : this.http.post<WorkShift>(this.base, body));
  }

  makeDefault(id: string): Promise<WorkShift> {
    return firstValueFrom(this.http.post<WorkShift>(`${this.base}/${id}/default`, {}));
  }

  setActive(id: string, active: boolean): Promise<WorkShift> {
    return firstValueFrom(this.http.post<WorkShift>(`${this.base}/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  /** Everyone's current and upcoming assignments - the people not on the default shift. */
  assignments(): Promise<readonly ShiftAssignment[]> {
    return firstValueFrom(this.http.get<readonly ShiftAssignment[]>(`${this.base}/assignments`));
  }

  /** Puts the person back on the default shift from `returnOn`. */
  async endAssignment(id: string, returnOn: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/assignments/${id}/end`, { returnOn }));
  }

  assign(body: { employeeIds: readonly string[]; shiftId: string; effectiveFrom: string; effectiveTo?: string | null }): Promise<{ assigned: number }> {
    return firstValueFrom(this.http.post<{ assigned: number }>(`${this.base}/assignments`, body));
  }

  roster(from: string, to: string, departmentId?: string | null, employeeId?: string | null): Promise<readonly RosterRow[]> {
    return firstValueFrom(this.http.get<readonly RosterRow[]>(`${this.base}/roster`, { params: params({ from, to, departmentId, employeeId }) }));
  }

  async setOverride(body: { employeeId: string; date: string; shiftId: string | null; reason?: string | null }): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/roster/overrides`, body));
  }

  async clearOverride(employeeId: string, date: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/roster/overrides`, { params: { employeeId, date } }));
  }

  swaps(): Promise<readonly ShiftSwap[]> {
    return firstValueFrom(this.http.get<readonly ShiftSwap[]>(`${this.base}/swaps`));
  }

  myShifts(from: string, to: string): Promise<readonly ScheduledDay[]> {
    return firstValueFrom(this.http.get<readonly ScheduledDay[]>('/api/v1/me/shifts', { params: { from, to } }));
  }

  mySwaps(): Promise<readonly ShiftSwap[]> {
    return firstValueFrom(this.http.get<readonly ShiftSwap[]>('/api/v1/me/shift-swaps'));
  }

  requestSwap(body: { counterpartEmployeeId: string; date: string; reason?: string | null }): Promise<ShiftSwap> {
    return firstValueFrom(this.http.post<ShiftSwap>('/api/v1/me/shift-swaps', body));
  }

  async cancelSwap(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/shift-swaps/${id}/cancel`, {}));
  }
}
