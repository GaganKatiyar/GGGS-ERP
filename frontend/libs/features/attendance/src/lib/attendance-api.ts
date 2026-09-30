import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AssignShiftRosterRequest,
  AssignWeeklyOffRequest,
  AttendanceDay,
  AttendancePunch,
  AttendanceRegularization,
  BiometricDevice,
  BiometricDeviceRegistered,
  CreateDeviceUserMapRequest,
  CreateShiftRequest,
  DeviceUserMap,
  OvertimeRequest,
  PagedResult,
  RecordPunchRequest,
  RegisterBiometricDeviceRequest,
  Shift,
  ShiftRoster,
  SubmitOvertimeRequestRequest,
  SubmitRegularizationRequest,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Attendance, over the gateway's `/api/v1/attendance/*` routes - mirrors `AttendanceEndpoints.cs`
 * on the server, the same one-service-per-module shape as `LeaveApi`.
 */
@Injectable({ providedIn: 'root' })
export class AttendanceApi {
  private readonly http = inject(HttpClient);

  // ---- Shifts ----

  listShifts(): Promise<readonly Shift[]> {
    return firstValueFrom(this.http.get<readonly Shift[]>('/api/v1/attendance/shifts'));
  }

  createShift(request: CreateShiftRequest): Promise<Shift> {
    return firstValueFrom(this.http.post<Shift>('/api/v1/attendance/shifts', request));
  }

  // ---- Roster ----

  listRoster(employeeId: string, fromDate: string, toDate: string): Promise<readonly ShiftRoster[]> {
    return firstValueFrom(
      this.http.get<readonly ShiftRoster[]>('/api/v1/attendance/rosters', {
        params: { employeeId, fromDate, toDate },
      }),
    );
  }

  assignRoster(request: AssignShiftRosterRequest): Promise<ShiftRoster> {
    return firstValueFrom(
      this.http.post<ShiftRoster>('/api/v1/attendance/rosters', request),
    );
  }

  assignWeeklyOff(request: AssignWeeklyOffRequest): Promise<ShiftRoster> {
    return firstValueFrom(
      this.http.post<ShiftRoster>('/api/v1/attendance/rosters/weekly-off', request),
    );
  }

  // ---- Punches ----

  recordPunch(request: RecordPunchRequest): Promise<AttendancePunch> {
    return firstValueFrom(
      this.http.post<AttendancePunch>('/api/v1/attendance/punches', request),
    );
  }

  checkIn(): Promise<AttendancePunch> {
    return firstValueFrom(
      this.http.post<AttendancePunch>('/api/v1/attendance/punches/me/check-in', {}),
    );
  }

  checkOut(): Promise<AttendancePunch> {
    return firstValueFrom(
      this.http.post<AttendancePunch>('/api/v1/attendance/punches/me/check-out', {}),
    );
  }

  // ---- Days ----

  listDays(employeeId: string, fromDate: string, toDate: string): Promise<readonly AttendanceDay[]> {
    return firstValueFrom(
      this.http.get<readonly AttendanceDay[]>('/api/v1/attendance/days', {
        params: { employeeId, fromDate, toDate },
      }),
    );
  }

  listMyDays(fromDate: string, toDate: string): Promise<readonly AttendanceDay[]> {
    return firstValueFrom(
      this.http.get<readonly AttendanceDay[]>('/api/v1/attendance/days/me', {
        params: { fromDate, toDate },
      }),
    );
  }

  // ---- Regularizations ----

  listRegularizations(query: {
    page: number;
    pageSize: number;
    employeeId?: string;
  }): Promise<PagedResult<AttendanceRegularization>> {
    return firstValueFrom(
      this.http.get<PagedResult<AttendanceRegularization>>(
        '/api/v1/attendance/regularizations',
        { params: toHttpParams(query) },
      ),
    );
  }

  submitRegularization(request: SubmitRegularizationRequest): Promise<AttendanceRegularization> {
    return firstValueFrom(
      this.http.post<AttendanceRegularization>('/api/v1/attendance/regularizations', request),
    );
  }

  cancelRegularization(id: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/attendance/regularizations/${id}/cancel`, {}),
    );
  }

  // ---- Overtime ----

  listOvertimeRequests(query: {
    page: number;
    pageSize: number;
    employeeId?: string;
  }): Promise<PagedResult<OvertimeRequest>> {
    return firstValueFrom(
      this.http.get<PagedResult<OvertimeRequest>>(
        '/api/v1/attendance/overtime-requests',
        { params: toHttpParams(query) },
      ),
    );
  }

  submitOvertimeRequest(request: SubmitOvertimeRequestRequest): Promise<OvertimeRequest> {
    return firstValueFrom(
      this.http.post<OvertimeRequest>('/api/v1/attendance/overtime-requests', request),
    );
  }

  cancelOvertimeRequest(id: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/attendance/overtime-requests/${id}/cancel`, {}),
    );
  }

  // ---- Biometric devices ----

  listDevices(): Promise<readonly BiometricDevice[]> {
    return firstValueFrom(this.http.get<readonly BiometricDevice[]>('/api/v1/attendance/devices'));
  }

  registerDevice(request: RegisterBiometricDeviceRequest): Promise<BiometricDeviceRegistered> {
    return firstValueFrom(
      this.http.post<BiometricDeviceRegistered>('/api/v1/attendance/devices', request),
    );
  }

  rotateDeviceApiKey(id: string): Promise<BiometricDeviceRegistered> {
    return firstValueFrom(
      this.http.post<BiometricDeviceRegistered>(`/api/v1/attendance/devices/${id}/rotate-key`, {}),
    );
  }

  deactivateDevice(id: string): Promise<BiometricDevice> {
    return firstValueFrom(
      this.http.post<BiometricDevice>(`/api/v1/attendance/devices/${id}/deactivate`, {}),
    );
  }

  reactivateDevice(id: string): Promise<BiometricDevice> {
    return firstValueFrom(
      this.http.post<BiometricDevice>(`/api/v1/attendance/devices/${id}/reactivate`, {}),
    );
  }

  listDeviceUserMaps(deviceId: string): Promise<readonly DeviceUserMap[]> {
    return firstValueFrom(
      this.http.get<readonly DeviceUserMap[]>(`/api/v1/attendance/devices/${deviceId}/user-maps`),
    );
  }

  createDeviceUserMap(deviceId: string, request: CreateDeviceUserMapRequest): Promise<DeviceUserMap> {
    return firstValueFrom(
      this.http.post<DeviceUserMap>(`/api/v1/attendance/devices/${deviceId}/user-maps`, request),
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
