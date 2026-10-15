/** Time contracts (M4): shifts and rosters, attendance, leave and holidays, as the API returns them. */

export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

export const WEEKDAYS: readonly Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

// ----- Shifts --------------------------------------------------------------------------------

export interface WorkShift {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  /** "HH:mm:ss" */
  readonly startTime: string;
  readonly endTime: string;
  readonly breakMinutes: number;
  readonly graceMinutes: number;
  readonly halfDayMinutes: number;
  readonly fullDayMinutes: number;
  readonly weeklyOff: readonly Weekday[];
  readonly isDefault: boolean;
  readonly isActive: boolean;
}

export interface WorkShiftInput {
  readonly code: string;
  readonly name: string;
  readonly startTime: string;
  readonly endTime: string;
  readonly breakMinutes: number;
  readonly graceMinutes: number;
  readonly halfDayMinutes: number;
  readonly fullDayMinutes: number;
  readonly weeklyOff: readonly Weekday[];
}

export interface ScheduledDay {
  readonly employeeId: string;
  readonly date: string;
  readonly shiftId?: string;
  readonly shiftName: string;
  readonly start: string;
  readonly end: string;
  readonly breakMinutes: number;
  readonly graceMinutes: number;
  readonly halfDayMinutes: number;
  readonly fullDayMinutes: number;
  readonly isWeeklyOff: boolean;
  readonly isOvernight: boolean;
}

export interface RosterRow {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly employeeCode: string;
  readonly days: readonly ScheduledDay[];
}

export type SwapStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface ShiftSwap {
  readonly id: string;
  readonly requesterEmployeeId: string;
  readonly requesterName?: string;
  readonly counterpartEmployeeId: string;
  readonly counterpartName?: string;
  readonly date: string;
  readonly reason?: string;
  readonly status: SwapStatus;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
}

// ----- Attendance ----------------------------------------------------------------------------

export type AttendanceStatus = 'Present' | 'HalfDay' | 'Absent' | 'WeeklyOff' | 'Holiday' | 'OnLeave' | 'MissingPunch';

export interface AttendanceDay {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly date: string;
  readonly shiftName: string;
  readonly scheduledStart: string;
  readonly scheduledEnd: string;
  readonly firstIn?: string;
  readonly lastOut?: string;
  readonly workedMinutes: number;
  readonly lateMinutes: number;
  readonly earlyLeaveMinutes: number;
  readonly status: AttendanceStatus;
  readonly punchCount: number;
  readonly workedOnDayOff: boolean;
  readonly isRegularized: boolean;
  readonly isToday: boolean;
  readonly note?: string;
}

/** An attendance day keyed for tables: one person, one date. */
export type AttendanceRow = AttendanceDay & { readonly id: string };

export function toAttendanceRows(days: readonly AttendanceDay[]): AttendanceRow[] {
  return days.map((day) => ({ ...day, id: `${day.employeeId}:${day.date}` }));
}

export interface PunchEntry {
  readonly at: string;
  readonly localTime: string;
  readonly source: 'Web' | 'Device' | 'Regularization' | 'Import';
}

export interface TodayAttendance {
  readonly day?: AttendanceDay;
  readonly punches: readonly PunchEntry[];
  readonly canPunch: boolean;
}

export interface AttendanceSummary {
  readonly date: string;
  readonly people: number;
  readonly byStatus: Readonly<Partial<Record<AttendanceStatus, number>>>;
  readonly late: number;
}

export type RegularizationStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface Regularization {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly date: string;
  readonly checkIn?: string;
  readonly checkOut?: string;
  readonly reason: string;
  readonly status: RegularizationStatus;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
}

export interface AttendanceDevice {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly locationId?: string;
  readonly lastSeenDate?: string;
  readonly isActive: boolean;
}

// ----- Leave ---------------------------------------------------------------------------------

export interface LeaveType {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isPaid: boolean;
  readonly allowHalfDay: boolean;
  readonly color: string;
  readonly isSystem: boolean;
  readonly isActive: boolean;
}

export type AccrualFrequency = 'None' | 'Monthly' | 'Yearly';

export interface LeavePolicy {
  readonly id: string;
  readonly leaveTypeId: string;
  readonly leaveTypeName: string;
  readonly employmentTypeCode?: string;
  readonly annualEntitlement: number;
  readonly accrual: AccrualFrequency;
  readonly carryForwardLimit?: number;
  readonly allowDuringProbation: boolean;
  readonly maxNegativeBalance: number;
  readonly maxConsecutiveDays?: number;
  readonly minNoticeDays: number;
  readonly countSandwichedDays: boolean;
  readonly isActive: boolean;
}

export type LeavePolicyInput = Omit<LeavePolicy, 'id' | 'leaveTypeName' | 'isActive'>;

export interface LeaveBalance {
  readonly leaveTypeId: string;
  readonly code: string;
  readonly name: string;
  readonly color: string;
  readonly isPaid: boolean;
  readonly balance: number;
  readonly pending: number;
  readonly available: number;
  readonly annualEntitlement: number;
  readonly takenThisYear: number;
}

export type LeaveRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface LeaveRequest {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly leaveTypeId: string;
  readonly leaveTypeCode: string;
  readonly leaveTypeName: string;
  readonly color: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly startsSecondHalf: boolean;
  readonly endsFirstHalf: boolean;
  readonly days: number;
  readonly reason?: string;
  readonly status: LeaveRequestStatus;
  readonly workflowInstanceId?: string;
  readonly appliedDate: string;
  readonly decidedDate?: string;
  readonly cancellationReason?: string;
}

export interface LeaveApplication {
  readonly leaveTypeId: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly startsSecondHalf: boolean;
  readonly endsFirstHalf: boolean;
  readonly reason?: string | null;
  readonly employeeId?: string | null;
}

export interface LeavePreview {
  readonly days: number;
  readonly available: number;
  readonly withinBalance: boolean;
  readonly problems: readonly string[];
}

export type LedgerKind = 'Accrual' | 'Grant' | 'Taken' | 'Restored' | 'Adjustment' | 'Lapse';

export interface LedgerEntry {
  readonly id: string;
  readonly entryDate: string;
  readonly kind: LedgerKind;
  readonly days: number;
  readonly note?: string;
  readonly requestId?: string;
}

export interface HolidayCalendar {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isDefault: boolean;
  readonly locationIds: readonly string[];
  readonly holidayCount: number;
}

export interface Holiday {
  readonly id: string;
  readonly calendarId: string;
  readonly date: string;
  readonly name: string;
  readonly isOptional: boolean;
}

export interface HolidayEntry {
  readonly date: string;
  readonly name: string;
  readonly isOptional: boolean;
}

/** "8h 05m" for a count of minutes; an empty string for none. */
export function formatMinutes(minutes: number): string {
  if (!minutes) {
    return '';
  }
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours}h ${String(rest).padStart(2, '0')}m` : `${rest}m`;
}

/** "09:05" from the API's "09:05:00". */
export function shortTime(time?: string): string {
  return time ? time.slice(0, 5) : '';
}
