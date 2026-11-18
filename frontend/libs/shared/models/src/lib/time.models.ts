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
  /** Current employees working it today: its own assignees, plus everyone unassigned on the default. */
  readonly people: number;
  /** False when only a punch in is expected: a working day with just that punch counts as a full day. */
  readonly requiresPunchOut: boolean;
}

/** "From this date (until that date) this person works that shift." */
export interface ShiftAssignment {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly shiftId: string;
  readonly shiftName: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
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
  readonly requiresPunchOut: boolean;
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
  readonly requiresPunchOut: boolean;
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
  /** False when the day's shift only needs a punch in. */
  readonly requiresPunchOut: boolean;
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

export type AccrualFrequency = 'None' | 'Monthly' | 'Quarterly' | 'HalfYearly' | 'Yearly';

/** When an accrual period's days are credited: on its first day, or once it has ended. */
export type CreditTiming = 'StartOfPeriod' | 'EndOfPeriod';

/** The rules one leave type follows inside a leave policy. Empty optional limits mean "no limit". */
export interface LeavePlanRules {
  readonly annualEntitlement: number;
  readonly accrual: AccrualFrequency;
  /** Days kept into the next leave year; empty keeps everything. */
  readonly carryForwardLimit?: number;
  readonly maxNegativeBalance: number;
  readonly countSandwichedDays: boolean;
  /** Gender lookup codes; empty means everyone. */
  readonly eligibleGenderCodes: readonly string[];
  readonly minServiceMonths: number;
  readonly allowDuringProbation: boolean;
  readonly allowDuringNotice: boolean;
  readonly minDaysPerRequest?: number;
  readonly maxConsecutiveDays?: number;
  readonly maxRequestsPerYear?: number;
  readonly minNoticeDays: number;
  readonly maxBackdatedDays?: number;
  readonly maxAdvanceDays?: number;
  /** A request longer than this waits for a supporting document. */
  readonly documentRequiredAboveDays?: number;
  readonly creditTiming: CreditTiming;
  readonly joiningCutoffDay: number;
  readonly prorateOnJoining: boolean;
  readonly carryForwardExpiryMonths?: number;
  readonly encashAtYearEnd: boolean;
  readonly maxEncashDays?: number;
  /** Days granted each time the leave is taken (maternity), instead of a balance. */
  readonly grantPerOccurrence?: number;
  readonly maxOccurrencesInEmployment?: number;
}

export interface LeavePlanRuleItem extends LeavePlanRules {
  readonly leaveTypeId: string;
  readonly leaveTypeCode: string;
  readonly leaveTypeName: string;
  readonly color: string;
  readonly isPaid: boolean;
  readonly allowHalfDay: boolean;
}

export interface LeavePlanItemInput extends LeavePlanRules {
  readonly leaveTypeId: string;
}

/** A named leave policy, as the list shows it. */
export interface LeavePlanSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  /** 1 = January ... 12 = December. */
  readonly leaveYearStartMonth: number;
  /** "Jan-Dec", "Apr-Mar". */
  readonly leaveYear: string;
  readonly isActive: boolean;
  readonly leaveTypeCount: number;
  /** Engagement-type codes this policy is assigned to. */
  readonly assignedTo: readonly string[];
  readonly exceptionCount: number;
  /** Names of the cost centers this policy is assigned to. */
  readonly assignedCostCenters?: readonly string[];
}

export interface LeavePlanDetail {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly leaveYearStartMonth: number;
  readonly leaveYear: string;
  readonly isActive: boolean;
  readonly assignedTo: readonly string[];
  readonly items: readonly LeavePlanRuleItem[];
  readonly assignedCostCenters?: readonly string[];
}

export interface LeavePlanInput {
  readonly code: string;
  readonly name: string;
  readonly description?: string | null;
  readonly leaveYearStartMonth: number;
}

/** One engagement type and the leave policy it follows. */
export interface LeavePlanAssignment {
  readonly employmentTypeCode: string;
  readonly label: string;
  readonly planId?: string;
  readonly planName?: string;
}

/**
 * One cost center and its own leave policies: `planId` for everyone on it (none: the organisation default),
 * and `overrides` for single engagement types there.
 */
export interface LeaveCostCenterAssignment {
  readonly costCenterId: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly companyName?: string;
  /** ISO country code: the cost center's own address's, else its company's. */
  readonly countryCode?: string;
  readonly employeeCount: number;
  readonly planId?: string;
  readonly planName?: string;
  readonly overrides?: readonly LeaveCostCenterTypeOverride[];
}

/** One engagement type on one cost center with a policy of its own. */
export interface LeaveCostCenterTypeOverride {
  readonly employmentTypeCode: string;
  readonly label: string;
  readonly planId: string;
  readonly planName?: string;
}

/** A change to one cost center's policy: no engagement type is its "everyone" policy; a null plan removes it. */
export interface LeaveCostCenterChange {
  readonly costCenterId: string;
  readonly employmentTypeCode?: string;
  readonly planId: string | null;
}

/** A single employee on another policy than their engagement type's, for a time. */
export interface EmployeeLeavePlan {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly planId: string;
  readonly planName: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly reason?: string;
}

export interface EmployeeLeavePlanInput {
  readonly employeeId: string;
  readonly planId: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string | null;
  readonly reason?: string | null;
}

/** The policy an employee follows today, and why. */
export interface ResolvedLeavePolicy {
  readonly planId: string;
  readonly code: string;
  readonly name: string;
  readonly leaveYear: string;
  readonly yearStart: string;
  readonly yearEnd: string;
  readonly source: 'EngagementType' | 'Exception' | 'CostCenter';
  readonly items: readonly LeavePlanRuleItem[];
  /** Set when the policy comes from the employee's cost center. */
  readonly costCenterName?: string;
}

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

export type LeaveRequestStatus = 'Draft' | 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

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
  readonly attachmentCount?: number;
}

/** A supporting document on a leave request. */
export interface LeaveAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly uploadedDate: string;
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
  /** The request will wait as a draft until a supporting document is attached. */
  readonly documentRequired?: boolean;
}

export type LedgerKind = 'Accrual' | 'Grant' | 'Taken' | 'Restored' | 'Adjustment' | 'Lapse' | 'Encashment';

export interface LedgerEntry {
  readonly id: string;
  readonly entryDate: string;
  readonly kind: LedgerKind;
  readonly days: number;
  readonly note?: string;
  readonly requestId?: string;
}

/** A way an employee may record attendance. */
export type AttendanceMode = 'Web' | 'Device' | 'Mobile';

export const ATTENDANCE_MODES: readonly { readonly value: AttendanceMode; readonly label: string; readonly hint: string }[] = [
  { value: 'Web', label: 'Web punch', hint: 'Punch in and out from the browser.' },
  { value: 'Device', label: 'Biometric device', hint: 'Punches from the office device.' },
  { value: 'Mobile', label: 'Mobile app', hint: 'Punch from the phone app.' },
];

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

/**
 * "09:05" from the API's "09:05:00", for an `<input type="time">`, which takes 24-hour values. To show
 * a time to people use `clockTime` from `@gggs/shared/ui`, which gives "9:05 AM".
 */
export function inputTime(time?: string): string {
  return time ? time.slice(0, 5) : '';
}
