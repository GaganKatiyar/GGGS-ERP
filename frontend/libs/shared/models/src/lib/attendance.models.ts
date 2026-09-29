/**
 * Attendance, mirroring the DTOs in `Gggs.Time.Api.Contracts.AttendanceContracts`.
 */

export interface Shift {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  /** `HH:mm:ss`, as `TimeOnly` serializes. */
  readonly startTime: string;
  readonly endTime: string;
  readonly breakMinutes: number;
  readonly isNightShift: boolean;
  readonly graceInMinutes: number;
  readonly graceOutMinutes: number;
  readonly fullDayMinMinutes: number;
  readonly halfDayMinMinutes: number;
}

export interface CreateShiftRequest {
  readonly code: string;
  readonly name: string;
  readonly startTime: string;
  readonly endTime: string;
  readonly breakMinutes: number;
  readonly isNightShift: boolean;
  readonly graceInMinutes: number;
  readonly graceOutMinutes: number;
  readonly fullDayMinMinutes: number;
  readonly halfDayMinMinutes: number;
}

export type RosterSource = 'Pattern' | 'Manual' | 'Swap';

export interface ShiftRoster {
  readonly id: string;
  readonly employeeId: string;
  readonly rosterDate: string;
  readonly shiftId?: string;
  readonly isWeeklyOff: boolean;
  readonly source: RosterSource;
}

export interface AssignShiftRosterRequest {
  readonly employeeId: string;
  readonly rosterDate: string;
  readonly shiftId: string;
}

export interface AssignWeeklyOffRequest {
  readonly employeeId: string;
  readonly rosterDate: string;
}

export type PunchDirection = 'In' | 'Out' | 'Auto';
export type PunchSource = 'Web' | 'Mobile' | 'Biometric' | 'Import' | 'Manual';

export interface AttendancePunch {
  readonly id: string;
  readonly employeeId: string;
  readonly punchTimeUtc: string;
  readonly direction: PunchDirection;
  readonly source: PunchSource;
  readonly isValid: boolean;
}

export interface RecordPunchRequest {
  readonly employeeId?: string;
  readonly punchTimeUtc: string;
  readonly direction: PunchDirection;
  readonly source: PunchSource;
  readonly deviceId?: string;
  readonly latitude?: number;
  readonly longitude?: number;
  readonly externalRef?: string;
}

export type AttendanceDayStatus =
  | 'Present'
  | 'Absent'
  | 'HalfDay'
  | 'OnLeave'
  | 'WeeklyOff'
  | 'Holiday'
  | 'OnDuty';

export const ATTENDANCE_DAY_STATUS_LABELS: Readonly<Record<AttendanceDayStatus, string>> = {
  Present: 'Present',
  Absent: 'Absent',
  HalfDay: 'Half day',
  OnLeave: 'On leave',
  WeeklyOff: 'Weekly off',
  Holiday: 'Holiday',
  OnDuty: 'On duty',
};

export interface AttendanceDay {
  readonly id: string;
  readonly employeeId: string;
  readonly attendanceDate: string;
  readonly shiftId?: string;
  readonly firstIn?: string;
  readonly lastOut?: string;
  readonly workedMinutes: number;
  readonly lateMinutes: number;
  readonly earlyOutMinutes: number;
  readonly status: AttendanceDayStatus;
  readonly leaveRequestId?: string;
  readonly isRegularized: boolean;
  readonly isLocked: boolean;
}

export type AttendanceRegularizationStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export const ATTENDANCE_REGULARIZATION_STATUS_LABELS: Readonly<
  Record<AttendanceRegularizationStatus, string>
> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
};

export interface AttendanceRegularization {
  readonly id: string;
  readonly employeeId: string;
  readonly attendanceDate: string;
  readonly requestedIn: string;
  readonly requestedOut: string;
  readonly reasonCode: string;
  readonly remarks?: string;
  readonly status: AttendanceRegularizationStatus;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
}

export interface SubmitRegularizationRequest {
  readonly employeeId?: string;
  readonly attendanceDate: string;
  readonly requestedIn: string;
  readonly requestedOut: string;
  readonly reasonCode: string;
  readonly remarks?: string;
}
