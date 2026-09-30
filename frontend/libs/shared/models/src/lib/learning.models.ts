/**
 * Learning, mirroring the DTOs in `Gggs.Talent.Api.Endpoints.CourseEndpoints` and
 * `EnrollmentEndpoints`.
 */

export interface CourseListItem {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly durationHours: number;
  readonly isActive: boolean;
}

export interface Course {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly description?: string;
  readonly durationHours: number;
  readonly isActive: boolean;
}

export interface CreateCourseRequest {
  readonly code: string;
  readonly title: string;
  readonly description?: string;
  readonly durationHours: number;
}

export type EnrollmentStatus = 'Assigned' | 'InProgress' | 'Completed' | 'Cancelled';

export interface EnrollmentListItem {
  readonly id: string;
  readonly courseId: string;
  readonly courseTitle: string;
  readonly dueDate?: string;
  readonly status: EnrollmentStatus;
  readonly completedDate?: string;
  readonly score?: number;
}

export interface Enrollment {
  readonly id: string;
  readonly courseId: string;
  readonly employeeId: string;
  readonly dueDate?: string;
  readonly status: EnrollmentStatus;
  readonly completedDate?: string;
  readonly score?: number;
}

export interface CreateEnrollmentRequest {
  readonly courseId: string;
  readonly employeeId: string;
  readonly dueDate?: string;
}

export interface CompleteEnrollmentRequest {
  readonly score?: number;
}
