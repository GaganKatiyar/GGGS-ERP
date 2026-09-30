/**
 * Reports, mirroring the DTOs in `Gggs.CoreHr.Api.Endpoints.ReportEndpoints` and
 * `Gggs.Time.Api.Endpoints.ReportEndpoints`.
 *
 * Reporting v1 reads straight off each owning service's own tables rather than a separate
 * event-fed read model (architecture.md's own read-model design is deferred until either data
 * volume or genuine cross-service reporting needs it - nothing publishes the events such a model
 * would consume yet).
 */

export interface HeadcountByDepartmentItem {
  readonly departmentId: string;
  readonly departmentName: string;
  readonly headcount: number;
}

export interface HeadcountByStatusItem {
  readonly status: string;
  readonly headcount: number;
}

export interface HeadcountReport {
  readonly totalActive: number;
  readonly joiners: number;
  readonly leavers: number;
  readonly byDepartment: readonly HeadcountByDepartmentItem[];
  readonly byStatus: readonly HeadcountByStatusItem[];
}

export interface AttendanceStatusCount {
  readonly status: string;
  readonly days: number;
}

export interface AttendanceSummaryReport {
  readonly fromDate: string;
  readonly toDate: string;
  readonly totalDays: number;
  readonly byStatus: readonly AttendanceStatusCount[];
  readonly regularizedDays: number;
}

export interface LeaveTypeSummary {
  readonly leaveTypeId: string;
  readonly leaveTypeName: string;
  readonly takenDays: number;
  readonly pendingDays: number;
}

export interface LeaveSummaryReport {
  readonly periodYear: number;
  readonly byType: readonly LeaveTypeSummary[];
}
