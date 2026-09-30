/**
 * Recruitment, mirroring the DTOs in `Gggs.Talent.Api.Endpoints.JobRequisitionEndpoints`.
 */

export type JobRequisitionStatus = 'Draft' | 'Open' | 'OnHold' | 'Closed' | 'Cancelled';

export interface JobRequisitionListItem {
  readonly id: string;
  readonly requisitionNo: string;
  readonly title: string;
  readonly openings: number;
  readonly status: JobRequisitionStatus;
}

export interface JobRequisition {
  readonly id: string;
  readonly requisitionNo: string;
  readonly title: string;
  readonly departmentId: string;
  readonly designationId: string;
  readonly branchId: string;
  readonly openings: number;
  readonly hiringManagerEmployeeId?: string;
  readonly hiringManagerDisplayName?: string;
  readonly justification?: string;
  readonly status: JobRequisitionStatus;
}

export interface CreateJobRequisitionRequest {
  readonly title: string;
  readonly departmentId: string;
  readonly designationId: string;
  readonly branchId: string;
  readonly openings: number;
  readonly hiringManagerEmployeeId?: string;
  readonly justification?: string;
}
