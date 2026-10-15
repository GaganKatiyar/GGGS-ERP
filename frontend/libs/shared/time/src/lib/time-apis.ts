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
  LeaveBalance,
  LeavePolicy,
  LeavePolicyInput,
  LeavePreview,
  LeaveRequest,
  LeaveRequestStatus,
  LeaveType,
  LedgerEntry,
  PagedResult,
  Regularization,
  RosterRow,
  ScheduledDay,
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

  policies(): Promise<readonly LeavePolicy[]> {
    return firstValueFrom(this.http.get<readonly LeavePolicy[]>(`${this.base}/policies`));
  }

  savePolicy(id: string | null, body: LeavePolicyInput): Promise<LeavePolicy> {
    return firstValueFrom(id ? this.http.put<LeavePolicy>(`${this.base}/policies/${id}`, body) : this.http.post<LeavePolicy>(`${this.base}/policies`, body));
  }

  async setPolicyActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/policies/${id}/${active ? 'activate' : 'deactivate'}`, {}));
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
