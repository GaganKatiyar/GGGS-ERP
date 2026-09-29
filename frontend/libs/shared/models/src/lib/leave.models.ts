/**
 * Leave, mirroring the DTOs in `Gggs.Time.Api.Contracts.LeaveContracts` and `HolidayContracts`.
 *
 * Every enum here is a string union, not a number: the server serializes every enum as its member
 * name (`JsonConfiguration.Apply` adds a `JsonStringEnumConverter` globally), same as
 * `organization.models.ts`'s `LocationType`.
 */

export type LeaveUnit = 'Day' | 'HalfDay' | 'Hour';

export interface LeaveTypeListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isPaid: boolean;
  readonly unit: LeaveUnit;
  readonly genderRestrictionCode?: string;
  readonly isCompOff: boolean;
  readonly isLop: boolean;
  readonly colorHex: string;
}

export interface CreateLeaveTypeRequest {
  readonly code: string;
  readonly name: string;
  readonly isPaid: boolean;
  readonly unit: LeaveUnit;
  readonly genderRestrictionCode?: string;
  readonly isCompOff: boolean;
  readonly isLop: boolean;
  readonly colorHex: string;
}

export interface LeavePolicyListItem {
  readonly id: string;
  readonly name: string;
  readonly version: number;
}

export interface CreateLeavePolicyRequest {
  readonly name: string;
}

export type AccrualFrequency = 'Monthly' | 'Quarterly' | 'Yearly' | 'OnJoining';

export interface LeavePolicyRuleListItem {
  readonly id: string;
  readonly policyId: string;
  readonly leaveTypeId: string;
  readonly accrualFrequency: AccrualFrequency;
  readonly accrualAmount: number;
  readonly proRataOnJoining: boolean;
  readonly maxBalance?: number;
  readonly carryForwardMax?: number;
  readonly carryForwardExpiryMonths?: number;
  readonly encashmentAllowed: boolean;
  readonly eligibleAfterDays: number;
  readonly minNoticeDays: number;
  readonly maxConsecutiveDays?: number;
  readonly minDaysPerRequest: number;
  readonly allowHalfDay: boolean;
  readonly allowNegativeBalance: boolean;
  readonly negativeLimit: number;
}

export interface CreateLeavePolicyRuleRequest {
  readonly leaveTypeId: string;
  readonly accrualFrequency: AccrualFrequency;
  readonly accrualAmount: number;
  readonly proRataOnJoining: boolean;
  readonly maxBalance?: number;
  readonly carryForwardMax?: number;
  readonly carryForwardExpiryMonths?: number;
  readonly encashmentAllowed: boolean;
  readonly eligibleAfterDays: number;
  readonly minNoticeDays: number;
  readonly maxConsecutiveDays?: number;
  readonly minDaysPerRequest: number;
  readonly allowHalfDay: boolean;
  readonly allowNegativeBalance: boolean;
  readonly negativeLimit: number;
}

export interface LeaveBalance {
  readonly leaveTypeId: string;
  readonly periodYear: number;
  readonly opening: number;
  readonly accrued: number;
  readonly taken: number;
  readonly pendingApproval: number;
  readonly adjusted: number;
  readonly encashed: number;
  readonly lapsed: number;
  readonly carriedForward: number;
  readonly available: number;
}

export type LeaveDaySession = 'Full' | 'First' | 'Second';

export type LeaveRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Withdrawn';

export const LEAVE_REQUEST_STATUS_LABELS: Readonly<Record<LeaveRequestStatus, string>> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  Withdrawn: 'Withdrawn',
};

export interface LeaveRequestListItem {
  readonly id: string;
  readonly requestNo: string;
  readonly employeeId: string;
  readonly leaveTypeId: string;
  readonly fromDate: string;
  readonly toDate: string;
  readonly totalDays: number;
  readonly status: LeaveRequestStatus;
}

export interface LeaveRequest {
  readonly id: string;
  readonly requestNo: string;
  readonly employeeId: string;
  readonly leaveTypeId: string;
  readonly fromDate: string;
  readonly fromSession: LeaveDaySession;
  readonly toDate: string;
  readonly toSession: LeaveDaySession;
  readonly totalDays: number;
  readonly reason?: string;
  readonly status: LeaveRequestStatus;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
}

export interface CreateLeaveRequestRequest {
  readonly employeeId?: string;
  readonly leaveTypeId: string;
  readonly fromDate: string;
  readonly fromSession: LeaveDaySession;
  readonly toDate: string;
  readonly toSession: LeaveDaySession;
  readonly reason?: string;
  readonly attachmentDocumentId?: string;
}

export interface HolidayCalendarListItem {
  readonly id: string;
  readonly name: string;
  readonly year: number;
  readonly countryCode: string;
  readonly stateCode?: string;
}

export interface CreateHolidayCalendarRequest {
  readonly name: string;
  readonly year: number;
  readonly countryCode: string;
  readonly stateCode?: string;
}

export interface HolidayListItem {
  readonly id: string;
  readonly holidayCalendarId: string;
  readonly holidayDate: string;
  readonly name: string;
  readonly isOptional: boolean;
  readonly isRestricted: boolean;
}

export interface AddHolidayRequest {
  readonly holidayDate: string;
  readonly name: string;
  readonly isOptional: boolean;
  readonly isRestricted: boolean;
}
