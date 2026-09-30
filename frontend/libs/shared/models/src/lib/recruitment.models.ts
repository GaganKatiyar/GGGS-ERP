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

export type JobPostingStatus = 'Draft' | 'Published' | 'Closed';

export interface JobPostingListItem {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly isPublic: boolean;
  readonly status: JobPostingStatus;
}

export interface JobPosting {
  readonly id: string;
  readonly requisitionId: string;
  readonly title: string;
  readonly descriptionHtml: string;
  readonly location?: string;
  readonly isInternal: boolean;
  readonly isPublic: boolean;
  readonly slug: string;
  readonly publishedDate?: string;
  readonly closesDate?: string;
  readonly status: JobPostingStatus;
}

export interface CreateJobPostingRequest {
  readonly title: string;
  readonly descriptionHtml: string;
  readonly location?: string;
  readonly isInternal: boolean;
  readonly isPublic: boolean;
  readonly slug: string;
  readonly closesDate?: string;
}

export type ApplicationStage = 'Applied' | 'Screening' | 'Interview' | 'Offer' | 'Hired' | 'Rejected';

export interface JobApplicationListItem {
  readonly id: string;
  readonly candidateId: string;
  readonly candidateDisplayName: string;
  readonly stage: ApplicationStage;
}

export interface JobApplication {
  readonly id: string;
  readonly postingId: string;
  readonly candidateId: string;
  readonly candidateDisplayName: string;
  readonly candidateEmail: string;
  readonly stage: ApplicationStage;
  readonly rejectionReason?: string;
}

export interface ApplyToPostingRequest {
  readonly firstName: string;
  readonly lastName?: string;
  readonly email: string;
  readonly phone?: string;
  readonly currentEmployer?: string;
  readonly totalExperienceMonths?: number;
  readonly source?: string;
}
